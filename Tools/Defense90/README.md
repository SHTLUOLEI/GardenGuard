# Defense90 字体子集

`prepare_font_subsets.py` 从完整原字体归档中生成候选字体，不直接写Unity工程。依赖现有Python环境中的fontTools和openpyxl。

在仓库根执行：

```powershell
python -X utf8 Tools/Defense90/prepare_font_subsets.py --source-archive GameRelease/Defense90/PackageOptimization-20261106/BASELINE.zip --output GameRelease/Defense90/PackageOptimization-20261106/FontCandidates
```

脚本扫描运行时源码、资源序列化文本、启动场景、配置JSON和GameConfig/Datas中的Excel字符串，解码文本中的Unicode转义；保留当前原字体可提供的所需字形及字体名称/许可信息。`FONT_COVERAGE.json`记录输入哈希、所需码点、候选字体哈希和缺失结果。

新增或变更游戏文字后，从归档的完整原字体重新生成候选，不使用已经裁剪过的字体作为源。确认覆盖检查通过后，通过Unity原生`import_asset`覆盖原路径，保留原GUID；再验证首页、设置、引导、结算及长短屏文字。任意玩家输入、额外语言和远程新增文本超出当前静态字符集，需要扩充文本来源或保留适合的完整字体。

原字体及许可文件保存在工程原目录和原始归档中；禁止用关闭Include Font Data代替子集化，那会改变中文字体可用性。
