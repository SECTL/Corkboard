using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Corkboard.Shared;

namespace Corkboard.Services.Desktop;

/// <summary>
///     单实例守门 + 参数转发：第二次启动（比如浏览器把 <c>corkboard://display</c> 交给系统时）
///     不再另起一份 Corkboard，而是把命令行交给已经跑着的那一份，自己退出。
///     <para>
///         为什么需要：注册 URL 协议之后，系统是「用这个命令行启动一个新进程」来响应链接的。
///         没有转发，点一次链接就会多开一个实例、两份程序抢同一批数据文件。
///     </para>
///     <para>
///         只用 BCL：一个命名 <see cref="Mutex" /> 判「我是不是第一个」，一条命名管道传命令行。
///         名字都按<b>数据根</b>算哈希——便携版与安装版的数据根不同，天然就是两个互不干扰的实例
///         （否则便携版会把自己塞进安装版那个进程里，写错一整套数据）。
///     </para>
///     <para>
///         转发失败时**不静默吞掉**：那条链接里的指令就丢了。所以失败就当自己也是第一实例继续跑
///         （宁可多开一个窗口，也不要「点了没反应」）。
///     </para>
/// </summary>
public static class SingleInstanceGate
{
    /// <summary>连那条管道最多等多久。第一实例可能还在建管道，重试几次就够。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>连不上时的重试次数（第一实例刚启动那一小段时间）。</summary>
    private const int ConnectAttempts = 3;

    private static readonly Queue<IReadOnlyList<string>> PendingArguments = new();

    /// <summary>管道线程与 UI 线程都会碰这两样，统一走这把锁。</summary>
    private static readonly object Sync = new();

    private static Mutex? _mutex;
    private static bool _serving;

    /// <summary>收到别的实例转发过来的命令行。可能在任意线程上触发，订阅方自己切线程。</summary>
    public static event Action<IReadOnlyList<string>>? ArgumentsReceived;

    /// <summary>本机的单实例标识（数据根的哈希），管道与互斥体都按它命名。</summary>
    public static string Identity
    {
        get
        {
            var root = Utils.DataRoot.ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(root));
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
    }

    private static string MutexName => $"corkboard-single-{Identity}";

    private static string PipeName => $"corkboard-args-{Identity}";

    /// <summary>
    ///     启动期的第一件事：已经有实例在跑就把命令行转过去并返回 <see langword="true" />（调用方应当退出），
    ///     否则占下互斥体、开始听管道并返回 <see langword="false" />。
    /// </summary>
    public static bool TryForwardToExistingInstance(IReadOnlyList<string> arguments)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
            if (!createdNew && TrySend(arguments))
                return true;

            // createdNew 为假但转发失败：那一份可能是启动到一半、也可能已经僵住。
            // 这时候当第一实例继续跑，至少链接里的指令还能生效（管道名会撞，先听失败的那个放弃即可）。
            StartListening();
            return false;
        }
        catch (Exception)
        {
            // 命名内核对象在受限环境里可能建不出来。转不了就当单实例在跑的那一份，
            // 也不要因为「守门」这件事本身把应用拦在启动之外。
            return false;
        }
    }

    /// <summary>取走启动期收到、但当时还没人订阅的那些命令行（订阅之后再来的走事件）。</summary>
    public static IReadOnlyList<IReadOnlyList<string>> DrainPendingArguments()
    {
        lock (Sync)
        {
            if (PendingArguments.Count == 0)
                return [];

            var items = PendingArguments.ToArray();
            PendingArguments.Clear();
            return items;
        }
    }

    /// <summary>
    ///     让出单实例（崩溃重启专用）：不去放这把命名 <see cref="Mutex" />，新进程就永远当不上第一实例，
    ///     会把命令行转发给这个垂死的进程、然后自己退出——表现就是「重启之后什么都没发生」。
    ///     互斥体从来没有 owned 过（<c>initiallyOwned: false</c>），所以只 Dispose、不 ReleaseMutex。
    /// </summary>
    public static void Release()
    {
        lock (Sync)
        {
            _serving = false;
        }

        ArgumentsReceived = null;

        try
        {
            _mutex?.Dispose();
        }
        catch (Exception)
        {
            // 已经释放过了：忽略。
        }
        finally
        {
            _mutex = null;
        }
    }

    /// <summary>开一条后台线程听管道。已经在听就什么都不做。</summary>
    private static void StartListening()
    {
        lock (Sync)
        {
            if (_serving)
                return;

            _serving = true;
        }

        var thread = new Thread(ServerLoop)
        {
            IsBackground = true,
            Name = "corkboard-single-instance"
        };
        thread.Start();
    }

    private static void ServerLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                server.WaitForConnection();

                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = reader.ReadToEnd();
                var arguments = SplitArguments(payload);
                if (arguments.Length > 0)
                    Dispatch(arguments);
            }
            catch (Exception)
            {
                // 管道被对方中途关掉之类的：这一笔丢了没关系，下一轮重新听。
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>把命令行交给订阅方；没人订阅就先排队（启动期那几百毫秒里就会发生）。</summary>
    private static void Dispatch(string[] arguments)
    {
        Action<IReadOnlyList<string>>? handler;
        lock (Sync)
        {
            handler = ArgumentsReceived;
            if (handler is null)
            {
                PendingArguments.Enqueue(arguments);
                return;
            }
        }

        try
        {
            handler(arguments);
        }
        catch (Exception)
        {
            // 转发方已经退出了，这里抛出去只会杀掉听管道的线程。
        }
    }

    /// <summary>把命令行写到第一实例那条管道上。写不进去返回 <see langword="false" />。</summary>
    private static bool TrySend(IReadOnlyList<string> arguments)
    {
        for (var attempt = 0; attempt < ConnectAttempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect((int)ConnectTimeout.TotalMilliseconds);

                using var writer = new StreamWriter(client, Encoding.UTF8);
                // 一个参数一行：参数里的换行在命令行上不合法，这里不需要转义。
                writer.Write(string.Join('\n', arguments));
                writer.Flush();
                return true;
            }
            catch (Exception)
            {
                // 第一实例还没把管道建好，稍等再试。
                Thread.Sleep(250);
            }
        }

        return false;
    }

    private static string[] SplitArguments(string payload) =>
        payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
