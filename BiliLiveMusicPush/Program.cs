using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFmpegSharp.Executor;
using NewLife.Json;
using NewLife.Log;
using NewLife.Threading;
using NewLife.Xml;

namespace BiliLiveMusicPush
{
    class Program
    {
        #region kernel32

        //委托
        private delegate int ConsoleCtrlDelegate(int CtrlType);

        //winApi
        [DllImport("kernel32.dll")]
        private static extern int SetConsoleCtrlHandler(ConsoleCtrlDelegate ctrlDelegate, int Add);

        //volatile static 变量防止优化
        volatile static ConsoleCtrlDelegate consoleCtrlDelegate = new ConsoleCtrlDelegate(HandlerRoutine);

        #endregion

        /// <summary>
        /// 释放Service资源
        /// </summary>
        /// <param name="ctrlType"></param>
        /// <returns></returns>
        private static int HandlerRoutine(int ctrlType)
        {
            switch (ctrlType)
            {
                case 0:
                case 2:
                    Process[] processesByName = Process.GetProcessesByName("ffmpeg");
                    if (processesByName.Any())
                    {
                        foreach (Process process in processesByName)
                        {
                            process.Kill();
                        }
                    }
                    break;
            }
            return 0;
        }

        static void Main(string[] args)
        {
            SetConsoleCtrlHandler(consoleCtrlDelegate, 1);
            XTrace.UseConsole();
            var currentDir =
                new FileInfo(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath));
            var appPath = currentDir.DirectoryName;

            if (string.IsNullOrWhiteSpace(appPath))
                throw new ApplicationException("app path not found.");
            if (!Directory.Exists(Config.Current.MusicPath))
                throw new ApplicationException("MusicPath path not found.");

            NewBiliRtmpPush();
            //TestPipeIn();
            //TestPipeOut();
            //RunBiliRtmpPush();
            while (true)
            {
                Console.ReadKey(false);
            }
        }

        private static void NewBiliRtmpPush()
        {
//            Thread bgPushMusic = new Thread(BgMusicPush);
//            bgPushMusic.Start();
//            Thread bgFFmpeg = new Thread(BgFFmpegRun);
//            bgFFmpeg.Start();
            ThreadPoolX.QueueUserWorkItem(BgMusicPush);
            ThreadPoolX.QueueUserWorkItem(BgFFmpegRun);
        }

        private static void BgFFmpegRun()
        {
            FileInfo LogoImage = new FileInfo(Config.Current.LogoImage);
            while (true)
            {
                if (Config.Current.rtmpUrl.IsNullOrWhiteSpace() || Config.Current.rtmpAuthKey.IsNullOrWhiteSpace())
                {
                    throw new ApplicationException("rtmp config is error.");
                }
                StringBuilder sb = new StringBuilder();
                //sb.Append(@" -threads 1");
                if (LogoImage.Exists)
                {
                    sb.Append($@" -r 15 -f image2 -loop 1 -i ""{LogoImage.FullName}"" ");
                }
                sb.Append($@" -re -i \\.\pipe\bilipush");
                if (LogoImage.Exists)
                {
                    //sb.Append($@" -shortest ");
                }
                //sb.Append($@" -c:a aac -ar 44100 -b:a 320k ");
                //sb.Append($@" -c:a copy -ar 44100 -b:a 320k ");
                if (LogoImage.Exists)
                {
                    //sb.Append($@" -c:v libx264 -pix_fmt yuvj420p");
                    sb.Append($@" -s 1920x1080 -pix_fmt yuvj420p -vcodec libx264 ");
                }
                sb.Append($@" -y -f flv {Config.Current.rtmpUrl}{Config.Current.rtmpAuthKey}");
                try
                {
                    XTrace.WriteLine(Processor.FFmpeg(sb.ToString()));
                }
                catch (Exception e)
                {
                    XTrace.WriteException(e);
                }
            }
        }

        private static void BgMusicPush()
        {
            List<string> played = new List<string>();
            try
            {
                while (true)
                {
                    using (NamedPipeServerStream pipeServer = new NamedPipeServerStream("bilipush", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.WriteThrough, 10000, 10000))
                    {
                        Console.WriteLine("NamedPipeServerStream object created.");
                        Console.WriteLine("Waiting for client connection...");
                        pipeServer.WaitForConnection();
                        Console.WriteLine("Client connected.");
                        try
                        {
                            while (true)
                            {
                                var files = Config.Current.MusicPath.GetFiles(Config.Current.MusicExt);
                                Console.WriteLine($"音乐读取完毕 共{files.Count}首");
                                while (pipeServer.IsConnected)
                                {
                                    if (pipeServer.CanWrite)
                                    {
                                        var file = files.Where(r => !played.Exists(f => f == r)).OrderBy(r => Guid.NewGuid()).FirstOrDefault();
                                        if (file == null)
                                        {
                                            played.Clear();
                                            continue;
                                        }
                                        FileInfo fileInfo = new FileInfo(file);
                                        byte[] readAllBytes = File.ReadAllBytes(file);
                                        Console.WriteLine($"正在推送音乐:{fileInfo.Name} 进度:{played.Count + 1}/{files.Count}");
                                        pipeServer.Write(readAllBytes);
                                        played.Add(file);
                                    }
                                }
                                break;
                            }
                        }
                        // Catch the IOException that is raised if the pipe is broken
                        // or disconnected.
                        catch (Exception e)
                        {
                            Console.WriteLine("ERROR: {0}", e.Message);
                            XTrace.WriteException(e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                XTrace.WriteException(e);
            }
        }

        private static void TestPipeIn()
        {
            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream("bilipush", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.WriteThrough, 10000, 10000))
            {
                Console.WriteLine("NamedPipeServerStream object created.");
                Console.Write("Waiting for client connection...");
                pipeServer.WaitForConnection();
                Console.WriteLine("Client connected.");
                var files = Config.Current.MusicPath.GetFiles(Config.Current.MusicExt);
                try
                {
                    while (true)
                    {
                        using (BinaryWriter bw = new BinaryWriter(pipeServer))
                        {
                            while (pipeServer.CanWrite)
                            {
                                foreach (var file in files)
                                {
                                    FileInfo fileInfo = new FileInfo(file);
                                    byte[] readAllBytes = File.ReadAllBytes(file);
                                    Console.WriteLine($"正在推送音乐:{fileInfo.Name}");
                                    bw.Write(readAllBytes);
                                }
                            }
                        }
                    }
                }
                // Catch the IOException that is raised if the pipe is broken
                // or disconnected.
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: {0}", e.Message);
                }
            }
        }

        private static void TestPipeOut()
        {
            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream("bilipush", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.WriteThrough, 10000, 10000))
            {
                Console.WriteLine("NamedPipeServerStream object created.");
                Console.Write("Waiting for client connection...");
                pipeServer.WaitForConnection();
                Console.WriteLine("Client connected.");
                byte[] bytes = new byte[1024];
                BinaryWriter bw = new BinaryWriter(new FileStream("text.flv", FileMode.Create));
                try
                {
                    while (true)
                    {
                        using (BinaryReader br = new BinaryReader(pipeServer))
                        {
                            // Display the read text to the console
                            int temp;
                            while (pipeServer.CanRead && (temp = br.Read(bytes, 0, bytes.Length)) > 0)
                            {
                                bw.Write(bytes, 0, temp);
                                Console.WriteLine("Received from server: {0}", bytes.ToHex(0, temp));
                            }
                        }
                    }
                }
                // Catch the IOException that is raised if the pipe is broken
                // or disconnected.
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: {0}", e.Message);
                }
            }
        }

        private static void RunBiliRtmpPush()
        {
            SetConsoleCtrlHandler(consoleCtrlDelegate, 1);
            XTrace.UseConsole();
            var currentDir =
                new FileInfo(Uri.UnescapeDataString(new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath));
            var appPath = currentDir.DirectoryName;

            if (string.IsNullOrWhiteSpace(appPath))
                throw new ApplicationException("app path not found.");
            if (!Directory.Exists(Config.Current.MusicPath))
                throw new ApplicationException("MusicPath path not found.");
            //DirectoryInfo MusicPath = new DirectoryInfo(Config.Current.MusicPath);
            FileInfo LogoImage = new FileInfo(Config.Current.LogoImage);
            while (true)
            {
                var files = Config.Current.MusicPath.GetFiles(Config.Current.MusicExt);
                if (!files.Any())
                {
                    throw new ApplicationException("MusicFiles is zero.");
                }
                Console.WriteLine($"音乐读取完毕 共{files.Count}首");

                var musicfile = files.OrderBy(r => Guid.NewGuid()).Join("|");
                if (Config.Current.rtmpUrl.IsNullOrWhiteSpace() || Config.Current.rtmpAuthKey.IsNullOrWhiteSpace())
                {
                    throw new ApplicationException("rtmp config is error.");
                }
//                    Network.Create()
//                        .WithSource(musicfile) //inputPath可以改成获取设备的视频流
//                        .WithDest($"{Config.Current.rtmpUrl}{Config.Current.rtmpAuthKey}") //可以根据自己的需求更新RTMP服务器地址
//                        //                .WithFilter(new X264Filter { ConstantQuantizer = 20 })
//                        //                .WithFilter(new ResizeFilter(Resolution.X360P))
//                        //                .WithFilter(new AudioRatelFilter(44100))
//                        .Push();
                StringBuilder sb = new StringBuilder();
                if (LogoImage.Exists)
                {
                    sb.Append($@" -loop 1 -y -i ""{LogoImage.FullName}"" ");
                }
                sb.Append($@" -re -i ""concat:{musicfile}"" ");
                if (LogoImage.Exists)
                {
                    sb.Append($@" -shortest ");
                }
                sb.Append($@" -c:a aac -ar 44100 -b:a 320k ");
                if (LogoImage.Exists)
                {
                    sb.Append($@" -c:v libx264 -pix_fmt yuvj420p -r 60");
                }
                sb.Append($@" -f flv {Config.Current.rtmpUrl}{Config.Current.rtmpAuthKey}");
                Processor.FFmpeg(sb.ToString());
                /*
                 ffmpeg -loop 1 -y -i "C:\Users\jay_jypeng\Pictures\6990.png"  -i "concat:D:\CloudMusic\EastNewSound - 死奏憐音、玲瓏ノ終.mp3|D:\CloudMusic\Ahxello - Frisbee.mp3" -shortest -acodec copy -vcodec libx264 -pix_fmt yuvj420p -f flv 1.flv 
                 */
            }
        }
    }

    public static class DirExt
    {
        public static List<string> GetFiles(this string dirPath, string[] exts)
        {
            return exts.SelectMany(ext => Directory.GetFiles(dirPath, $"*.{ext}", SearchOption.AllDirectories).ToList()).ToList();
        }
    }

    [JsonConfigFile("config.json")]
    public class Config : JsonConfig<Config>
    {
        /// <summary>
        /// 直播地址
        /// </summary>
        public string rtmpUrl { get; set; }

        /// <summary>
        /// 直播Key
        /// </summary>
        public string rtmpAuthKey { get; set; }

        /// <summary>
        /// 音乐存放目录
        /// </summary>
        public string MusicPath { get; set; } = @"D:\CloudMusic";

        /// <summary>
        /// 背景图片
        /// </summary>
        public string LogoImage { get; set; } = @"./reload.png";

        /// <summary>
        /// 音乐后缀
        /// </summary>
        public string[] MusicExt { get; set; } = new[] {"mp3", "ogg"};
    }
}