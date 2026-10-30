# PluginSmoke — 插件管理冒烟测试

不需要 OBS、不需要联网，用来验证「插件一键安装 / 更新 / 回滚 / 卸载」整条事务链路。

```powershell
dotnet run --project NOBS/tools/PluginSmoke/PluginSmoke.csproj -c Debug
```

它用一个假的 `HttpClient` 返回**内存里的合成 zip**，安装到临时目录，逐项断言：

| 检查 | 说明 |
|---|---|
| 安装 | 解压、写 `installed.json`、SHA-256 |
| 覆盖安装 | 旧版本进入 `backups` |
| 回滚 | 恢复上一版本文件 |
| 卸载 | 移入备份、清单清空、不硬删 |
| OBS 运行中 | 拒绝安装（`PLUGIN_OBS_RUNNING`） |
| zip slip | 拒绝（`PLUGIN_ZIP_UNSAFE`） |
| 32 位原生 DLL | 拒绝（`PLUGIN_BAD_ARCH`） |
| 哈希不匹配 | 拒绝（`PLUGIN_HASH_MISMATCH`） |

> 这是开发向工具，不在 `OBS_Helper.slnx` 里，也不会进发布物。
> 真实 GitHub 下载 / 签名 / 杀软行为仍需要人工在真实环境中验证。
