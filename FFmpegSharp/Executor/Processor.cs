using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NewLife.Log;

namespace FFmpegSharp.Executor
{
    public class Processor
    {
        public static string FFmpeg(string @params, Action<int, string> onStart = null)
        {
            return Execute(true, @params, onStart);
        }

        public static string FFprobe(string @params, bool is_push, Action<int, string> onStart = null)
        {
            //当参数1是false的时候则为获取视频流的信息
            //当参数1是true的时候则为获取视频流

            return Execute(is_push, @params, onStart);
        }

        private static string Execute(bool userFFmpeg, string @params, Action<int, string> onStart = null)
        {
            Process p = null;
            try
            {
                using (p = new Process())
                {
                    var workdir = Path.GetDirectoryName(Config.Instance.FFmpegPath);

                    if (string.IsNullOrWhiteSpace(workdir))
                        throw new ApplicationException("work directory is null");

                    var exePath = userFFmpeg ? Config.Instance.FFmpegPath : Config.Instance.FFprobePath;
                    string fullCmd = $"{exePath} {@params}";
                    var info = new ProcessStartInfo(exePath)
                    {
                        Arguments              = @params,
                        CreateNoWindow         = true,
                        RedirectStandardError  = true,
                        RedirectStandardOutput = true,
                        UseShellExecute        = false,
                        Domain                 = ".",
                        WorkingDirectory       = workdir,
                        StandardErrorEncoding  = Encoding.UTF8,
                        StandardOutputEncoding = Encoding.UTF8,
                    };
                    p.StartInfo = info;
                    p.Start();

                    if (null != onStart)
                    {
                        onStart.Invoke(p.Id, fullCmd);
                    }

                    var message = string.Empty;
                    if (userFFmpeg)
                    {
                        while (!p.StandardError.EndOfStream)
                        {
                            message = p.StandardError.ReadLine();
                            //Console.WriteLine(message);
                        }
                    }
                    else
                    {
                        message = p.StandardOutput.ReadToEnd();
                        //Console.WriteLine(message);
                    }

                    p.WaitForExit();
                    return message;
                }
            }
            finally
            {
                if (null != p)
                {
                    p.Close();
                    p.Dispose();
                }
            }
        }
    }
}