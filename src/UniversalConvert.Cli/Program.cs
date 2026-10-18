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
                if (options.Errors.Count == 0
                    && options.Command != CliCommand.Help
                    && options.Command != CliCommand.Version)
                {
                    var compatibility = CliCompatibility.Check();
                    if (!string.IsNullOrEmpty(compatibility))
                    {
                        Console.Error.WriteLine(compatibility);
                        return CliExitCode.InternalError;
                    }
                }

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
            catch (MissingMethodException)
            {
                Console.Error.WriteLine(CliText.ComponentMismatchUnknown);
                return CliExitCode.InternalError;
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
