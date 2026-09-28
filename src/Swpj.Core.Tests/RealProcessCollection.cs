namespace Swpj.Core.Tests;

/// <summary>
/// 会真的启动外部进程、动真格压文件的测试，放进同一个**不并行**的集合。
///
/// 理由：xUnit 默认让不同测试类并行跑。这些测试每个都占满 CPU（真实 7z 压 48 MB）、
/// 还会杀进程树，并发时互相抢资源，会出现"单独跑必过、全量跑偶发失败"的情况——
/// 实测踩过一次（`EndToEndTests.用真实_7zip_跑通压缩_校验_解压全流程` 与新增的取消测试撞车）。
/// 本项目对偶发失败的容忍度是零：宁可慢几十秒，也不要一个会随机红的门禁。
/// </summary>
[CollectionDefinition(RealProcessCollection.Name, DisableParallelization = true)]
public sealed class RealProcessCollection
{
    public const string Name = "真实进程";
}
