using System.Globalization;

namespace Telescope.Simulator;

/// <summary>커맨드라인 옵션. PROTOCOL.md 2장(포트), 11.1장(--seed, --ccd-drift) 기준.</summary>
public sealed record Options(int Port, int Seed, double CcdDrift)
{
    public static Options Parse(string[] args)
    {
        var port = 5000;
        int? seed = null;
        var drift = 0.02; // °C/s, ICD 11.1 기본값

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    port = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--seed":
                    seed = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--ccd-drift":
                    drift = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"알 수 없는 옵션: {args[i]}");
            }
        }

        // --seed를 안 주면 임의 시드를 쓰되, 시작 로그에 찍어서 나중에 재현할 수 있게 한다.
        return new Options(port, seed ?? Random.Shared.Next(), drift);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{args[i]} 뒤에 값이 필요합니다.");
        return args[++i];
    }
}
