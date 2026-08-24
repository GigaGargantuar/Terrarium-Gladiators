using System;
using System.IO;

try
{
    using var game = new TerrariumGladiators.Game1();
    game.Run();
}
catch (Exception exception)
{
    // A WinExe has no console, so preserve future startup failures beside the
    // executable instead of disappearing without a useful diagnostic.
    var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
    File.WriteAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
    throw;
}
