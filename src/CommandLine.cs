using System;
using System.Collections.Generic;
using System.IO;

namespace Brighter
{
    /// <summary>One command-line instruction, e.g. "--set 2 40".</summary>
    internal sealed class Command
    {
        public string Kind = "";   // set | contrast | volume | temp | profile
        public int Target;         // monitor number as shown in the app, 0 = all
        public int Value;
        public string Name = "";   // profile name
    }

    /// <summary>
    /// Command line:
    ///   Brighter.exe --set 2 40          brightness of monitor 2 to 40 %
    ///   Brighter.exe --set all 70        every monitor to 70 %
    ///   Brighter.exe --contrast 1 60
    ///   Brighter.exe --volume 1 20
    ///   Brighter.exe --temp all 4500     colour temperature in Kelvin (3400 - 6500)
    ///   Brighter.exe --profile Night
    /// Several can be combined. When Brighter is already running the commands are handed to it;
    /// otherwise they are executed directly and the process exits.
    /// </summary>
    internal static class CommandLine
    {
        public static string CommandFile
        {
            get { return Path.Combine(SettingsStore.Folder, "command.txt"); }
        }

        public static List<Command> Parse(string[] args)
        {
            List<Command> list = new List<Command>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = (args[i] ?? "").Trim().ToLowerInvariant();
                if ((arg == "--set" || arg == "--contrast" || arg == "--volume" || arg == "--temp") && i + 2 < args.Length)
                {
                    Command command = new Command();
                    command.Kind = arg.Substring(2);
                    string target = (args[i + 1] ?? "").Trim();
                    int number;
                    int amount;
                    bool targetOk = true;
                    if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
                        command.Target = 0;
                    else if (int.TryParse(target, out number) && number > 0)
                        command.Target = number;
                    else
                        targetOk = false;

                    if (targetOk && int.TryParse((args[i + 2] ?? "").Trim(), out amount))
                    {
                        command.Value = amount;
                        list.Add(command);
                    }
                    i += 2;
                }
                else if (arg == "--profile" && i + 1 < args.Length)
                {
                    Command command = new Command();
                    command.Kind = "profile";
                    command.Name = (args[i + 1] ?? "").Trim();
                    list.Add(command);
                    i += 1;
                }
            }
            return list;
        }

        /// <summary>Executes commands without any window (no other Brighter instance is running).</summary>
        public static void RunHeadless(List<Command> commands)
        {
            List<MonitorDevice> devices = MonitorService.Enumerate();
            AppSettings settings = SettingsStore.Load();
            try
            {
                foreach (Command command in commands)
                {
                    if (command.Kind == "profile")
                    {
                        foreach (ProfileData profile in settings.Profiles)
                        {
                            if (!string.Equals(profile.Name, command.Name, StringComparison.OrdinalIgnoreCase)) continue;
                            foreach (MonitorDevice device in devices)
                            {
                                foreach (ProfileEntry entry in profile.Entries)
                                {
                                    if (entry.MonitorId != device.Id) continue;
                                    device.ApplyNow(entry.Brightness, entry.Contrast, -1);
                                    if (entry.Temperature >= 0) GammaRamp.Apply(device.GdiName, entry.Temperature);
                                    break;
                                }
                            }
                            break;
                        }
                        continue;
                    }

                    foreach (MonitorDevice device in devices)
                    {
                        if (command.Target != 0 && command.Target != device.Index) continue;
                        if (command.Kind == "set") device.ApplyNow(command.Value, -1, -1);
                        else if (command.Kind == "contrast") device.ApplyNow(-1, command.Value, -1);
                        else if (command.Kind == "volume") device.ApplyNow(-1, -1, command.Value);
                        else if (command.Kind == "temp") GammaRamp.Apply(device.GdiName, GammaRamp.LevelFromKelvin(command.Value));
                    }
                }
            }
            finally
            {
                foreach (MonitorDevice device in devices)
                    device.Dispose();
            }
        }
    }
}
