// 콘솔 로그를 음성으로 출력하는 간단한 데모
using System.Diagnostics;
using System.Speech.Synthesis;

var options = ParseArgs(args);
var message = options.Message ?? "안녕하세요, 콘솔 메시지를 음성으로 출력합니다.";
Console.WriteLine(message);

Speak(message, options.Voice);

if (!string.IsNullOrWhiteSpace(options.OutputPath))
{
    var fullPath = GetFullPathSafe(options.OutputPath);
    if (fullPath is null)
    {
        Console.Error.WriteLine($"지정한 경로를 처리할 수 없습니다: {options.OutputPath}");
    }
    else if (!TryCreateAudioFile(message, options.Voice, fullPath))
    {
        Console.Error.WriteLine($"음성 파일을 생성하지 못했습니다: {fullPath}");
    }
    else if (options.PlayOutput && !TryPlayAudioFile(fullPath))
    {
        Console.Error.WriteLine($"생성된 음성 파일을 재생하지 못했습니다: {fullPath}");
    }
}

static AppOptions ParseArgs(string[] args)
{
    string? message = null;
    string? voice = null;
    string? output = null;
    var playFromFile = false;

    for (var i = 0; i < args.Length; i++)
    {
        if (TryReadOption(args, ref i, "m", "message", out var value, out var missingValue))
        {
            if (missingValue)
            {
                Console.Error.WriteLine("옵션 -m/--message 에 메시지 값을 제공해 주세요.");
                continue;
            }

            message = value;
            continue;
        }

        if (TryReadOption(args, ref i, "v", "voice", out value, out missingValue))
        {
            if (missingValue)
            {
                Console.Error.WriteLine("옵션 -v/--voice 에 음성 이름을 제공해 주세요.");
                continue;
            }

            voice = value;
            continue;
        }

        if (TryReadOption(args, ref i, "o", "output", out value, out missingValue))
        {
            if (missingValue)
            {
                Console.Error.WriteLine("옵션 -o/--output 에 파일 경로를 제공해 주세요.");
                continue;
            }

            output = value;
            continue;
        }

        var current = args[i];
        if (current is "-p" or "--play")
        {
            playFromFile = true;
            continue;
        }

        if (current.StartsWith("--play=", StringComparison.Ordinal))
        {
            var boolPart = current["--play=".Length..];
            if (bool.TryParse(boolPart, out var parsed))
            {
                playFromFile = parsed;
            }
            else
            {
                Console.Error.WriteLine("옵션 --play 는 true/false 로 지정할 수 있습니다.");
            }
            continue;
        }

        Console.Error.WriteLine($"알 수 없는 옵션을 무시합니다: {current}");
    }

    return new AppOptions(message, voice, output, playFromFile);
}

static bool TryReadOption(string[] args, ref int index, string shortName, string longName, out string? value, out bool missingValue)
{
    var current = args[index];
    var shortOption = "-" + shortName;
    var longOption = "--" + longName;

    if (current == shortOption || current == longOption)
    {
        if (index + 1 < args.Length)
        {
            value = args[++index];
            missingValue = false;
        }
        else
        {
            value = null;
            missingValue = true;
        }
        return true;
    }

    if (current.StartsWith(shortOption + "=", StringComparison.Ordinal))
    {
        value = current[(shortOption.Length + 1)..];
        missingValue = false;
        return true;
    }

    if (current.StartsWith(longOption + "=", StringComparison.Ordinal))
    {
        value = current[(longOption.Length + 1)..];
        missingValue = false;
        return true;
    }

    value = null;
    missingValue = false;
    return false;
}

static void Speak(string text, string? voice)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        if (!string.IsNullOrWhiteSpace(voice) && TryRunProcess("say", "-v", voice, text))
        {
            return;
        }

        if (!TryRunProcess("say", text))
        {
            Console.Error.WriteLine("macOS 음성 합성에 실패했습니다.");
        }
        return;
    }

    if (OperatingSystem.IsWindows())
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            if (!string.IsNullOrWhiteSpace(voice))
            {
                try
                {
                    synth.SelectVoice(voice);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"지정한 음성을 사용할 수 없습니다 ({voice}): {ex.Message}");
                }
            }
            synth.Speak(text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Windows 음성 합성에 실패했습니다: {ex.Message}");
        }
        return;
    }

    if (OperatingSystem.IsLinux())
    {
        // Linux에서는 espeak 또는 spd-say 같은 TTS 도구가 설치되어 있다고 가정
        if (!string.IsNullOrWhiteSpace(voice) && TryRunProcess("espeak", "-v", voice, text))
        {
            return;
        }

        if (TryRunProcess("espeak", text))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(voice) && TryRunProcess("spd-say", "-v", voice, text))
        {
            return;
        }

        if (TryRunProcess("spd-say", text))
        {
            return;
        }

        Console.Error.WriteLine("Linux 음성 합성을 위한 명령어(espeak, spd-say)를 찾지 못했습니다.");
        return;
    }

    Console.WriteLine("음성 출력은 이 운영체제에서 지원되지 않습니다.");
}

static bool TryCreateAudioFile(string text, string? voice, string outputPath)
{
    try
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"출력 경로를 준비하지 못했습니다: {ex.Message}");
        return false;
    }

    if (OperatingSystem.IsMacOS())
    {
        var extension = Path.GetExtension(outputPath);
        var wantsWave = string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase);

        var targetForSay = outputPath;
        string? temporaryAiff = null;

        if (wantsWave)
        {
            var fileName = Path.GetFileNameWithoutExtension(outputPath);
            var directory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
            temporaryAiff = Path.Combine(directory, $"{fileName}_tmp.aiff");
            targetForSay = temporaryAiff;
        }

        var arguments = new List<string> { "-o", targetForSay, text };
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var withVoice = new List<string> { "-v", voice };
            withVoice.AddRange(arguments);
            if (TryRunProcess("say", withVoice.ToArray()))
            {
                return wantsWave ? ConvertAiffToWave(temporaryAiff!, outputPath) : true;
            }

            Console.Error.WriteLine($"지정한 음성을 사용할 수 없어 기본 음성으로 생성합니다: {voice}");
        }

        if (TryRunProcess("say", arguments.ToArray()))
        {
            return wantsWave ? ConvertAiffToWave(temporaryAiff!, outputPath) : true;
        }

        Console.Error.WriteLine("macOS 음성 파일 생성에 실패했습니다.");
        return false;
    }

    if (OperatingSystem.IsWindows())
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            if (!string.IsNullOrWhiteSpace(voice))
            {
                try
                {
                    synth.SelectVoice(voice);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"지정한 음성을 사용할 수 없어 기본 음성으로 생성합니다 ({voice}): {ex.Message}");
                }
            }
            synth.SetOutputToWaveFile(outputPath);
            synth.Speak(text);
            synth.SetOutputToDefaultAudioDevice();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Windows 음성 파일 생성에 실패했습니다: {ex.Message}");
            return false;
        }
    }

    if (OperatingSystem.IsLinux())
    {
        var baseArguments = new List<string> { "-w", outputPath, text };
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var withVoice = new List<string> { "-v", voice };
            withVoice.AddRange(baseArguments);
            if (TryRunProcess("espeak", withVoice.ToArray()))
            {
                return true;
            }

            Console.Error.WriteLine($"지정한 음성을 사용할 수 없어 기본 음성으로 생성합니다: {voice}");
        }

        if (TryRunProcess("espeak", baseArguments.ToArray()))
        {
            return true;
        }

        Console.Error.WriteLine("Linux 환경에서 espeak 으로 음성 파일을 생성하지 못했습니다.");
        return false;
    }

    Console.Error.WriteLine("이 운영체제에서는 음성 파일 생성을 지원하지 않습니다.");
    return false;
}

static bool TryPlayAudioFile(string outputPath)
{
    if (!File.Exists(outputPath))
    {
        Console.Error.WriteLine("재생할 파일이 존재하지 않습니다.");
        return false;
    }

    if (OperatingSystem.IsMacOS())
    {
        if (TryRunProcess("afplay", outputPath))
        {
            return true;
        }

        Console.Error.WriteLine("macOS에서 음성 파일 재생에 실패했습니다 (afplay).");
        return false;
    }

    if (OperatingSystem.IsWindows())
    {
        var script = $"(New-Object Media.SoundPlayer '{EscapeSingleQuotes(outputPath)}').PlaySync();";
        if (TryRunProcess("powershell", "-NoProfile", "-Command", script))
        {
            return true;
        }

        Console.Error.WriteLine("Windows에서 음성 파일 재생에 실패했습니다.");
        return false;
    }

    if (OperatingSystem.IsLinux())
    {
        if (TryRunProcess("aplay", outputPath) || TryRunProcess("paplay", outputPath))
        {
            return true;
        }

        Console.Error.WriteLine("Linux에서 음성 파일 재생에 실패했습니다 (aplay/paplay).");
        return false;
    }

    Console.Error.WriteLine("이 운영체제에서는 파일 재생을 지원하지 않습니다.");
    return false;
}

static bool TryRunProcess(string fileName, params string[] arguments)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        process?.WaitForExit();
        return process is not null && process.ExitCode == 0;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"프로세스 실행 실패: {ex.Message}");
        return false;
    }
}

static string? GetFullPathSafe(string path)
{
    try
    {
        return Path.GetFullPath(path);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"경로를 계산하는 중 오류가 발생했습니다: {ex.Message}");
        return null;
    }
}

static string EscapeSingleQuotes(string value) => value.Replace("'", "''");

static bool ConvertAiffToWave(string aiffPath, string wavePath)
{
    try
    {
        if (!File.Exists(aiffPath))
        {
            Console.Error.WriteLine("변환 대상 AIFF 파일이 존재하지 않습니다.");
            return false;
        }

        var arguments = new[]
        {
            "-f", "WAVE",
            "-d", "LEI16@44100",
            aiffPath,
            wavePath
        };

        if (!TryRunProcess("afconvert", arguments))
        {
            Console.Error.WriteLine("AIFF 파일을 WAV로 변환하지 못했습니다 (afconvert).");
            return false;
        }

        File.Delete(aiffPath);
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"AIFF→WAV 변환 중 오류가 발생했습니다: {ex.Message}");
        return false;
    }
}

internal sealed record AppOptions(string? Message, string? Voice, string? OutputPath, bool PlayOutput);
