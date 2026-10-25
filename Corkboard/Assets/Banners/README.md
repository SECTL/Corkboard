# 关于页海报（Banner）

这个目录留给「设置 → 关于」页顶部那块大海报。**目前不放图**，只把目录与文件名约定先定下来；
等海报做出来之后按下面的名字丢进来即可，代码不用改（见 `AboutSettingsPage.LoadBanner`）。

| 文件名 | 用在哪 |
| --- | --- |
| `corkboard-banner-cn.png` | 界面语言是中文时 |
| `corkboard-banner-ja.png` | 界面语言是日文时 |
| `corkboard-banner-en.png` | 其它语言（含英文）时 |

约定：

- 尺寸与上游 SecRandom-C 那三张一致（约 2000×500，横长条），`Stretch="Uniform"` 铺进页头，不用自己留白。
- 图里不要写文案以外的关键信息：页头会随滚动淡出，被裁掉的部分不可读。
- 三张可以只放一部分：缺哪张就退回英文那张，英文也缺就整块页头退化成纯标题（不会崩）。
- 新图要同时补 `THIRD-PARTY-NOTICES.md` 的「随包分发的资源」一行（自制的写自制）。
