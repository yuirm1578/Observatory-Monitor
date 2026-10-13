using System.Globalization;

namespace Telescope.Simulator;

/// <summary>한 시점의 장비 상태 (PROTOCOL.md 4장 DATA 필드).</summary>
public sealed record Sample(
    double Temp, int Hum, double Wind, double Alt, double Az,
    string State, string Dome, int Ilk)
{
    /// <summary>DATA 한 줄. 숫자는 로케일 영향을 받지 않도록 InvariantCulture로 쓴다 (ICD 3장).</summary>
    public string ToDataLine() => string.Create(CultureInfo.InvariantCulture,
        $"DATA,TEMP={Temp:F1},HUM={Hum},WIND={Wind:F1},ALT={Alt:F1},AZ={Az:F1},STATE={State},DOME={Dome},ILK={Ilk}");
}

/// <summary>
/// 장비 대역 모델 (PROTOCOL.md 11.2).
/// M1 범위: 센서값만 움직이고 STATE/DOME/ALT/AZ는 고정이다. 상태 머신은 M2에서 얹는다.
/// </summary>
public sealed class DeviceModel
{
    private readonly Random _rng;
    private readonly double _ccdDrift;

    // CCD 온도: "기준선 + 노이즈"로 모델링한다. 노이즈를 누적하지 않아야
    // 선형 상승 구간의 창 표준편차가 ICD 9.2에서 가정한 값(약 0.35 °C)이 된다.
    private double _tempBaseline = -20.0;
    private double _hum = 60.0;
    private double _wind = 3.0;

    public DeviceModel(int seed, double ccdDrift)
    {
        _rng = new Random(seed);
        _ccdDrift = ccdDrift;
    }

    public Sample Step()
    {
        _tempBaseline += _ccdDrift;
        var temp = _tempBaseline + Gaussian(0.1);

        // 평균 회귀 랜덤워크: 평균 쪽으로 조금씩 끌어당기고 노이즈를 더한다.
        _hum += 0.05 * (60.0 - _hum) + Gaussian(0.8);
        _wind += 0.05 * (3.0 - _wind) + Gaussian(0.3);
        _hum = Math.Clamp(_hum, 0, 100);
        _wind = Math.Clamp(_wind, 0, 60);

        return new Sample(
            Temp: temp,
            Hum: (int)Math.Round(_hum),
            Wind: _wind,
            Alt: 45.0,
            Az: 180.0,
            State: "IDLE",
            Dome: "CLOSED",
            Ilk: 0);
    }

    /// <summary>Box–Muller 변환으로 만든 N(0, sigma²) 난수.</summary>
    private double Gaussian(double sigma)
    {
        var u1 = 1.0 - _rng.NextDouble(); // (0, 1]
        var u2 = _rng.NextDouble();
        return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
