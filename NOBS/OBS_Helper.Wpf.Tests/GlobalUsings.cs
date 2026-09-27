global using Xunit;

// 关闭测试并行（V2.9.2）。
//
// 原因：当前语言是**进程级可变状态**（Strings.Current），而大量断言直接比对本地化后的成品文案。
// xUnit 默认按集合并行跑测试类，一旦有测试切到英文（StringsTests 里就需要切），并发执行的
// 其它断言会拿到英文文案而随机失败 —— 表现为「单跑通过、全量跑挂」的偶发抖动。
//
// 全套测试在当前规模下串行只要约 1 秒，用确定性换这点时间非常划算。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
