using System;
using System.Text;

namespace UniversalConvert.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
                // 某些宿主环境不支持修改控制台编码，不影响执行
            }

            CliOptions options = null;
            try
            {
                options = CliOptions.Parse(args);
                var runner = new CliRunner(options);
                var code = runner.Run();
                Environment.ExitCode = code;
                return code;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine(CliText.Cancelled);
                return CliExitCode.Cancelled;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format(CliText.InternalError, ex.Message));
                if (options != null && options.Verbose)
                {
                    Console.Error.WriteLine(ex);
                }
                return CliExitCode.InternalError;
            }
        }
    }
}
