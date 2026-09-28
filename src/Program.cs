using System;
using System.Threading;
using System.Windows.Forms;

namespace DesktopCat
{
    public static class Program
    {
        const string InstanceName = "DesktopCat.Kotik";

        [STAThread]
        public static int Main()
        {
            AppNative.EnsureDpiAwareness();
            InstallCrashHandlers();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch { }

            try
            {
                return Finish(RunPet());
            }
            catch (Exception ex)
            {
                CatLog.Error("Main", ex);
                return 1;
            }
        }

        static int Finish(int code)
        {
            // a leftover background thread must never keep an invisible process alive
            Environment.Exit(code);
            return code;
        }

        static void InstallCrashHandlers()
        {
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); } catch { }
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                CatLog.Error("UI thread (ignored, app keeps running)", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                CatLog.Error("unhandled (terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
            };
        }

        static int RunPet()
        {
            Mutex mutex = null;
            bool owns = false;
            EventWaitHandle callEvent = null;
            try
            {
                try
                {
                    bool createdNew;
                    mutex = new Mutex(true, @"Local\" + InstanceName + ".SingleInstance", out createdNew);
                    owns = createdNew;
                    if (!owns)
                    {
                        try { owns = mutex.WaitOne(0); }
                        catch (AbandonedMutexException) { owns = true; }   // previous instance died
                    }
                }
                catch (Exception ex) { CatLog.Error("single-instance mutex", ex); owns = true; }

                try { callEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + InstanceName + ".Call"); }
                catch (Exception ex) { CatLog.Error("call event", ex); }

                if (!owns)
                {
                    CatLog.Write("already running: asking the running cat to come to the cursor");
                    try { if (callEvent != null) callEvent.Set(); } catch { }
                    return 0;
                }

                AppController app = new AppController(callEvent);
                Application.Run(app);
                return 0;
            }
            catch (Exception ex)
            {
                CatLog.Error("RunPet", ex);
                return 1;
            }
            finally
            {
                if (mutex != null)
                {
                    if (owns) { try { mutex.ReleaseMutex(); } catch { } }
                    try { mutex.Dispose(); } catch { }
                }
                if (callEvent != null) { try { callEvent.Dispose(); } catch { } }
            }
        }
    }
}
