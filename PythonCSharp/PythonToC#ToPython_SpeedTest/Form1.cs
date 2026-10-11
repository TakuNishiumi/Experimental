using OpenCvSharp;
using Python.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
//using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PythonToC_ToPython_SpeedTest
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // 1. Pythonの初期化
            // 環境に合わせてPythonのDLLパスやPythonHomeを設定してください
            Runtime.PythonDLL = @"C:\Users\Taku\anaconda3\envs\gsplat\python312.dll"; // 環境に合わせて変更
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();

            using (Py.GIL())
            {
                try
                {
                    // モジュールをロード
                    dynamic sys = Py.Import("sys");
                    sys.path.append($"C:\\Users\\Taku\\source\\repos\\github\\実験場\\比較_C#-Pythonデータやり取り\\Python"); // 現在のディレクトリをパスに追加
                    dynamic mainModule = Py.Import("main_module");

                    // テスト画像の作成 (1920x1080, 3チャンネル / 2K解像度)
                    int width = 1920;
                    int height = 1080;
                    Mat originalMat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(128));
                    // テスト用に少し模様を描画
                    Cv2.Circle(originalMat, new Point(width / 2, height / 2), 200, new Scalar(0, 0, 255), -1);

                    int dataSize = (int)(originalMat.Total() * originalMat.ElemSize());
                    int iterations = 100; // ベンチマークのループ回数

                    Console.WriteLine($"=== 速度比較テスト (画像サイズ: {width}x{height}, 試行回数: {iterations}回) ===");

                    // ==========================================
                    // 方式 A: 共有メモリ (SharedMemory) を使った方式
                    // ==========================================
                    // .NETの MemoryMappedFile を使って共有メモリを作成
                    using (var mmf = MemoryMappedFile.CreateOrOpen("MyImageSharedMemory", dataSize))
                    {
                        // Python側に共有メモリの名前を渡して初期化させる
                        mainModule.init_shared_memory("MyImageSharedMemory", dataSize);

                        // 一度バイト配列を用意しておく（ループごとに確保するオーバーヘッドを避けるため外側で定義）
                        byte[] managedArray = new byte[dataSize];

                        Stopwatch sw = Stopwatch.StartNew();
                        for (int i = 0; i < iterations; i++)
                        {
                            // 1. Matのポインタ (originalMat.Data) から byte[] へコピー
                            Marshal.Copy(originalMat.Data, managedArray, 0, dataSize);

                            // 2. C#側の画像を共有メモリへ書き込む
                            using (var accessor = mmf.CreateViewAccessor())
                            {
                                // 型引数として byte を指定するか、byte[] を渡す
                                accessor.WriteArray<byte>(0, managedArray, 0, dataSize);
                            }

                            // 3. Python側の処理を実行（左右反転など）
                            mainModule.process_shared_memory();

                            // 4. 共有メモリから結果を読み込む場合
                            Mat resultMat = new Mat(height, width, MatType.CV_8UC3);
                            using (var accessor = mmf.CreateViewAccessor())
                            {
                                accessor.ReadArray<byte>(0, managedArray, 0, dataSize);
                            }
                            // 必要に応じて resultMat.Data へ書き戻す
                            Marshal.Copy(managedArray, 0, resultMat.Data, dataSize);

                            // 最後のループ（例として1回目など）で画像を保存してみる
                            if (i == 0)
                            {
                                Cv2.ImWrite($"C:\\Users\\Taku\\source\\repos\\github\\実験場\\比較_C#-Pythonデータやり取り\\Output\\flipped_output_sharedmemory.png", resultMat);
                            }

                            // 解放
                            resultMat.Dispose();
                        }


                        sw.Stop();
                        Console.WriteLine($"[1. SharedMemory方式] 処理時間: {sw.ElapsedMilliseconds} ms");
                    }

                    // ==========================================
                    // 方式 B: ポインタ直渡し (ゼロコピー) 方式
                    // ==========================================
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        for (int i = 0; i < iterations; i++)
                        {
                            IntPtr ptr = originalMat.Data;

                            // Python側で左右反転処理が行われ、ポインタが返ってくる
                            dynamic processedData = mainModule.process_zero_copy(ptr.ToInt64(), width, height, dataSize);

                            IntPtr resultPtr = (IntPtr)(long)processedData;

                            // メモリコピーなしでC#のMatとしてラップ
                            using (Mat resultMatZeroCopy = Mat.FromPixelData(height, width, MatType.CV_8UC3, resultPtr))
                            {
                                // 最後のループ（例として1回目など）で画像を保存してみる
                                if (i == 0)
                                {
                                    Cv2.ImWrite($"C:\\Users\\Taku\\source\\repos\\github\\実験場\\比較_C#-Pythonデータやり取り\\Output\\flipped_output_pointer.png", resultMatZeroCopy);
                                }
                            }
                        }
                        sw.Stop();
                        Console.WriteLine($"[2. ゼロコピー (ポインタ直渡し) 方式] 処理時間: {sw.ElapsedMilliseconds} ms");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"エラーが発生しました: {ex.Message}");
                }
            }

            PythonEngine.Shutdown();
            Console.WriteLine("テスト終了。何かキーを押してください...");
        }
    }
}
