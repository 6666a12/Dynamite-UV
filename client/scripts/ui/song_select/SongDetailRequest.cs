using DynamiteUniverse.Shared.IO;

namespace DynamiteUniverse.Ui;

/// <summary>详情请求身份：代际号 + 包 id + 谱面 id。加载结果带着它回传，调用方据此做身份自查。</summary>
public readonly record struct SongDetailKey(int Generation, string PackId, string ChartId);

/// <summary>
/// 选曲详情请求的代际与单请求加载器（纯 C#，不引入引擎、会话或谱面包对象）。
///
/// 拥有代际号与 <see cref="LatestRequestLoader{TKey,TValue}"/>：同一时刻至多一个在途或待取的详情任务，
/// 每次刷新推进代际并取消旧请求，旧代际的完成结果既不会被发布、也不会通过身份校验。
///
/// 边界：结果载荷用 <typeparamref name="TChart"/> 泛型表达，本对象不认识它；主线程消费、
/// 当前选中项解析、预写缓存与展示都留在调用方。代际只在调用方线程推进（与原有字段同口径，
/// 不引入新的线程假设），完成结果由 <see cref="LatestRequestLoader{TKey,TValue}"/> 在自己的锁内聚合。
/// </summary>
public sealed class SongDetailRequest<TChart> : IDisposable where TChart : class
{
    private readonly LatestRequestLoader<SongDetailKey, TChart> _loader = new();
    private int _generation;
    private bool _disposed;

    /// <summary>当前请求代际：试听的身份快照与卡片换文的代际守卫都用它。</summary>
    public int CurrentGeneration => _generation;

    /// <summary>开始一次新刷新：取消在途/待取的旧请求并推进代际，返回本次请求的代际号。</summary>
    public int Begin() => AdvanceGeneration();

    /// <summary>作废当前代际（收起卡等）：取消在途/待取的请求并推进代际，
    /// 让尚未消费的完成结果与旧的试听身份一起失效。</summary>
    public int Invalidate() => AdvanceGeneration();

    /// <summary>代际是否仍是当前的（已释放的对象不再有当前代际）。</summary>
    public bool IsCurrentGeneration(int generation) => !_disposed && generation == _generation;

    /// <summary>结果身份是否仍属于当前请求：代际当前，且包 id / 谱面 id 与调用方给出的当前身份一致。
    /// 当前身份由调用方解析后传入，本对象不读全局选择。</summary>
    public bool IsCurrent(SongDetailKey key, string packId, string chartId) =>
        IsCurrentGeneration(key.Generation) &&
        string.Equals(key.PackId, packId, StringComparison.Ordinal) &&
        string.Equals(key.ChartId, chartId, StringComparison.Ordinal);

    /// <summary>启动一次详情加载（会取消上一次请求）。返回的任务由 loader 吞掉取消与异常，
    /// 结果（含错误）经 <see cref="TryTakeCompleted"/> 在调用方线程取走。</summary>
    public Task Start(SongDetailKey key, Func<TChart> load) => _loader.Start(key, load);

    /// <summary>取走唯一的完成结果（错误以数据回传）；没有完成结果时返回 false。</summary>
    public bool TryTakeCompleted(out RequestLoadResult<SongDetailKey, TChart> result) =>
        _loader.TryTakeCompleted(out result);

    /// <summary>取消在途请求并释放 loader。此后不再接受新请求
    /// （<see cref="Start"/> 抛 <see cref="ObjectDisposedException"/>），代际也不再视为当前。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loader.Dispose();
    }

    private int AdvanceGeneration()
    {
        _loader.Cancel();
        return ++_generation;
    }
}
