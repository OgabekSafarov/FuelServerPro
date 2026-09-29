using System;
using System.IO;

namespace FuelServerPro
{
    public static class Logger
    {
        private static readonly object SyncRoot = new();

        public static void Log(string message)
        {
            lock (SyncRoot)
            {
                try
                {
                    string dir = @"C:\FuelServerLogs";
                    Directory.CreateDirectory(dir);

                    string filePath = Path.Combine(dir, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
                    string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";

                    File.AppendAllText(filePath, logLine + Environment.NewLine);
                    Console.WriteLine(logLine);
                }
                catch
                {
                    // Logging should never crash the application.
                }
            }
        }
    }
}
