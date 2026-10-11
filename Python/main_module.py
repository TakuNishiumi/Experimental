import numpy as np
import ctypes
from multiprocessing import shared_memory

_shm = None
_shm_np_array = None

def init_shared_memory(shm_name, size):
    global _shm, _shm_np_array
    try:
        _shm = shared_memory.SharedMemory(name=shm_name)
        _shm_np_array = np.ndarray((size,), dtype=np.uint8, buffer=_shm.buf)
    except Exception as e:
        print(f"共有メモリの初期化エラー: {e}")

def process_shared_memory():
    global _shm_np_array
    if _shm_np_array is not None:
        # 共有メモリ用の反転サンプル
        img = _shm_np_array.reshape((1080, 1920, 3))
        processed = np.ascontiguousarray(np.fliplr(img))
        _shm_np_array[:] = processed.ravel()

def process_zero_copy(data_ptr, width, height, size):
    """
    C#から渡されたポインタを直接操作し、左右反転して返す
    """
    array_type = ctypes.c_ubyte * size
    c_array = array_type.from_address(data_ptr)
    
    # コピーゼロでNumPy配列化
    img_np = np.frombuffer(c_array, dtype=np.uint8).reshape((height, width, 3))
    
    # 左右反転処理 (インプレース、またはcontiguousな配列にして書き戻す)
    # np.fliplr はビューを返すため、元のバッファに安全に書き戻す
    img_flipped = np.ascontiguousarray(np.fliplr(img_np))
    
    # Python側で新しく確保された配列のデータポインタ（整数）を取得
    new_ptr = img_flipped.ctypes.data    

    # 【重要】Pythonのガベージコレクションによって img_flipped が勝手に解放され、
    # C#側が参照する前にメモリが消えるのを防ぐため、モジュール内の変数等に一度保持しておくか、
    # あるいは pythonnet を通して渡すことで参照を維持させます。
    # ここでは安全のためにグローバル変数に最新の参照を保持しておきます
    global _last_result_img
    _last_result_img = img_flipped    
    
    return new_ptr