// 콘솔 로그를 음성으로 출력하는 간단한 데모
using System.Diagnostics;
using System.Speech.Synthesis;

var options = ParseArgs(args);
var message = options.Message ?? "안녕하세요, 콘솔 메시지를 음성으로 출력합니다.";
Console.WriteLine(message);
Speak(message, options.Voice);

static AppOptions ParseArgs(string[] args)
{
    string? message = null;
    string? voice = null;

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
        }
    }

    return new AppOptions(message, voice);
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

internal sealed record AppOptions(string? Message, string? Voice);
