using System.Net;
using System.Net.Sockets;
using System.Text;
using Telescope.Simulator;

// M1 블록 1 범위: 접속을 받아서 1초마다 DATA 한 줄을 보낸다. (명령 처리는 M2)
Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("사용법: Telescope.Simulator [--port 5000] [--seed N] [--ccd-drift 0.02]");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 프로세스를 바로 죽이지 않고 정리 후 종료
    cts.Cancel();
};

var listener = new TcpListener(IPAddress.Any, options.Port);
try
{
    listener.Start();
}
catch (SocketException ex)
{
    Console.Error.WriteLine($"포트 {options.Port}을 열 수 없습니다: {ex.Message}");
    return 1;
}

Console.WriteLine($"[sim] listening on :{options.Port}  seed={options.Seed}  ccd-drift={options.CcdDrift}");
Console.WriteLine("[sim] Ctrl+C 로 종료");

Task? session = null;
try
{
    while (!cts.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);

        // ICD 2장: 동시 1 클라이언트. 이미 접속 중이면 새 접속은 즉시 close.
        if (session is { IsCompleted: false })
        {
            Console.WriteLine($"[sim] 접속 거절 {client.Client.RemoteEndPoint} (이미 접속 중)");
            client.Close();
            continue;
        }

        // 접속마다 모델을 새로 만든다 → 같은 --seed면 매 접속이 같은 값 순서를 보낸다.
        session = RunSessionAsync(client, new DeviceModel(options.Seed, options.CcdDrift), cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C
}
finally
{
    listener.Stop();
    if (session is not null)
    {
        try { await session; } catch { /* 종료 중 오류는 무시 */ }
    }
}

Console.WriteLine("[sim] 종료");
return 0;

static async Task RunSessionAsync(TcpClient client, DeviceModel model, CancellationToken ct)
{
    var remote = client.Client.RemoteEndPoint;
    using var _ = client;
    Console.WriteLine($"[sim] 접속 {remote}");

    try
    {
        var stream = client.GetStream();

        // 수신 처리는 M2에서 한다. 지금은 상대가 끊었는지만 알아내려고 읽어서 버린다.
        var drain = DrainAsync(stream, ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)); // DATA 주기 1초 (ICD 4장)
        while (!drain.IsCompleted && await timer.WaitForNextTickAsync(ct))
        {
            var line = model.Step().ToDataLine();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), ct); // 한 줄 = 한 메시지, LF 종단
            Console.WriteLine($"TX {line}");
        }
    }
    catch (OperationCanceledException) { }
    catch (IOException) { }          // 상대가 끊음
    catch (ObjectDisposedException) { }
    finally
    {
        Console.WriteLine($"[sim] 접속 종료 {remote}");
    }
}

static async Task DrainAsync(NetworkStream stream, CancellationToken ct)
{
    var buffer = new byte[256];
    try
    {
        while (await stream.ReadAsync(buffer, ct) > 0) { }
    }
    catch (IOException) { }
    catch (ObjectDisposedException) { }
    catch (OperationCanceledException) { }
}
