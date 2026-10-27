using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BigInteger = System.Numerics.BigInteger;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain
{
    private string? _digestAudioPath;
    private string? _digestAudioSha;
    // DIGEST 音频哈希的后台任务状态：任务只算字节哈希，写回 _digestAudioSha/_digestAudioPath
    // 一律在主线程 CallDeferred 里做。_digestPending 是「是否有进行中的任务」+ 代际守卫用的目标
    // 期望（任务启动时记录，完成时与之一致才落结果）；_digestLastMeta 是上次算过哈希时的文件
    // 元数据，路径相同但元数据变了就重算（同路径换内容不吞过期哈希）。
    private bool _digestPending;
    private string? _digestExpectedPath;
    private string? _digestAudioMetaKey;

    /// <summary>DIGEST 盾标：用当前编辑状态真算一次 gameplay-v1 digest（内存谱面投影 + 音频字节的
    /// SHA-256），与保存时 writer 写入的身份同一算法。音频哈希按「路径 + 文件元数据（最后写入时间 +
    /// 长度）」缓存，换包/保存/文件被替换后才重算；哈希计算在后台线程做，主线程只读元数据判断
    /// 失效与派发任务（首次进 Pack 页不整读音频）。
    /// 取不到音频（无音频引用的包）或谱面当前校验不过（例如还没有主判定）时返回 false，
    /// 页面画压暗的 `DIGEST —`，不假装通过。异步化后第一次调用哈希未就绪同样返回 false，
    /// 哈希就绪后由 <see cref="ApplyDigestAudioSha"/> 的回调再刷一次盾标。</summary>
    private bool TryDigest()
    {
        if (_document is null) return false;
        try
        {
            var audio = ResolveDigestAudio();
            if (audio is null)
            {
                // 没有音频引用：清掉缓存与进行中任务的期望，盾标维持压暗。
                InvalidateDigestAudioHash();
                return false;
            }
            EnsureDigestAudioHash(audio);
            if (!string.Equals(_digestAudioPath, audio, StringComparison.OrdinalIgnoreCase) ||
                _digestAudioSha is null)
                return false; // 哈希未就绪（后台任务进行中/失败），盾标先画压暗的 `DIGEST —`。
            var entry = _document.BuildPackSnapshot().Charts.Single(item => item.Id == _document.SelectedChartId);
            _ = V2GameplayDigest.Compute(_document.BuildChartSnapshot(_document.SelectedChartId),
                entry.JudgePreset, _digestAudioSha);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>保证 <paramref name="audio"/> 的字节 SHA-256 已缓存且不过期：元数据（最后写入时间 +
    /// 长度）与缓存键一致就直接用；路径或元数据变了就在后台线程重算，完成时 CallDeferred 回主线程
    /// 落 <see cref="_digestAudioSha"/>/<see cref="_digestAudioPath"/> 并刷新盾标。
    /// 元数据读取很轻，留在主线程做失效判断；同一路径已有任务在跑时不重复起任务。</summary>
    private void EnsureDigestAudioHash(string audio)
    {
        var metaKey = DigestAudioMetaKey(audio);
        if (!_digestPending &&
            string.Equals(_digestAudioPath, audio, StringComparison.OrdinalIgnoreCase) &&
            _digestAudioSha is not null &&
            string.Equals(_digestAudioMetaKey, metaKey, StringComparison.Ordinal))
            return; // 缓存命中：路径与元数据都没变，直接沿用。
        if (_digestPending && string.Equals(_digestExpectedPath, audio, StringComparison.OrdinalIgnoreCase))
            return; // 同一路径的任务已在跑，等它回来即可。
        // 派发新任务（可能是首次、路径变了或元数据变了）：记下期望与元数据键。
        _digestPending = true;
        _digestExpectedPath = audio;
        _digestAudioMetaKey = metaKey;
        var expected = audio;
        _ = Task.Run(() =>
        {
            // CallDeferred 走 Godot 消息队列，可跨线程调用，回调在主线程执行；
            // 不装 SynchronizationContext 的 Godot 主线程上 FromCurrentSynchronizationContext 会直接抛。
            var sha = DigestAudioHashOnBackground(expected);
            CallDeferred(nameof(ApplyDigestAudioSha), expected,
                sha is null ? "" : Convert.ToHexStringLower(sha));
        });
    }

    /// <summary>后台线程只读文件字节并算 SHA-256，绝不碰 Godot 对象；读文件失败（被删等）时
    /// 返回 null，由主线程静默放弃，不落缓存，盾标维持压暗的 `DIGEST —`。</summary>
    private static byte[]? DigestAudioHashOnBackground(string path)
    {
        try
        {
            return SHA256.HashData(File.ReadAllBytes(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>后台任务的回调：只在主线程执行（任务里 CallDeferred 回到这里），
    /// 用代际守卫丢弃过期结果——期望路径已变（换包、保存后又触发）或节点已释放时一律不落缓存。
    /// 哈希就绪后触发一次盾标重绘（Pack 页重画 <c>DIGEST OK</c>）。</summary>
    private void ApplyDigestAudioSha(string path, string sha256Hex)
    {
        // CallDeferred 到已释放的节点会被 Godot 静默丢弃，但这里显式守一下更稳。
        if (!IsInstanceValid(this)) return;
        // 期望已被清掉（换包/保存的失效点），或回来的不是当前期望那份：丢弃过期结果。
        if (!_digestPending ||
            !string.Equals(_digestExpectedPath, path, StringComparison.OrdinalIgnoreCase))
            return;
        _digestPending = false;
        _digestExpectedPath = null;
        if (sha256Hex.Length == 0 ||
            !string.Equals(path, ResolveDigestAudio(), StringComparison.OrdinalIgnoreCase))
            return; // 读文件失败，或期望路径已不是当前需要的音频：静默放弃，不落缓存。
        if (!string.Equals(_digestAudioMetaKey, DigestAudioMetaKey(path), StringComparison.Ordinal))
        {
            // 哈希途中文件被替换（元数据已变）：这份结果是旧内容的，重派一次拿新内容的。
            EnsureDigestAudioHash(path);
            return;
        }
        _digestAudioSha = sha256Hex;
        _digestAudioPath = path;
        // 哈希就绪：盾标从压暗的 `DIGEST —` 变 `DIGEST OK`，Pack 页重绘一次。
        if (_flowPage == EditorFlowPage.Pack) RefreshPackPage();
    }

    /// <summary>让 digest 音频哈希缓存与进行中的后台任务一起失效（开包/保存/音频引用消失时调用）：
    /// 进行中的任务完成时会因 _digestPending 已清而丢弃结果。</summary>
    private void InvalidateDigestAudioHash()
    {
        _digestAudioSha = null;
        _digestAudioPath = null;
        _digestAudioMetaKey = null;
        _digestExpectedPath = null;
        _digestPending = false;
    }

    /// <summary>digest 音频缓存的元数据键：最后写入时间（UTC）+ 文件长度。路径相同但元数据变了就
    /// 重算，同路径换内容不会用过期哈希；元数据读取很轻，留在主线程做失效判断。</summary>
    private static string? DigestAudioMetaKey(string audio)
    {
        try
        {
            var info = new FileInfo(audio);
            return $"{File.GetLastWriteTimeUtc(audio):O}|{info.Length}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>digest 用的音频源：已落盘包走 writer 的严格包内解析（差分自身音频优先），
    /// 内存草稿用 draft 的外部音频——两者都是保存时会写进包的那份字节。</summary>
    private string? ResolveDigestAudio()
    {
        return _documentSession.ResolveDigestAudio();
    }
}
