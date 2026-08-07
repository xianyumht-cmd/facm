# FACM 动画桌宠素材清单

本文件记录 FACM 内置动画桌宠的来源、作者和许可证。正式内置目录只接受许可证明确且允许重新分发/商用的素材；当前第一批统一使用 CC0。

| FACM 宠物 | 作者 | 来源页 | 动画结构 | 许可证 |
| --- | --- | --- | --- | --- |
| 猫咪 | alizard | https://opengameart.org/content/pixel-cat-0 | 5 帧跑动 | CC0 |
| 狗狗 | rmazanek / Shepardskin / Hellkipz | https://opengameart.org/content/dog-3 | 6×6 动画表，FACM 使用 Walk 行 | CC0 |
| 蜘蛛 | KillGorack | https://opengameart.org/content/iso-spider-spritesheet | 8 方向 × 13 帧 = 104 帧 | CC0 |
| 蚂蚁 | DudeMan | https://opengameart.org/content/walking-ant-with-parts-and-rigged-spriter-file | 多方向行走 Sprite Sheet | CC0 |
| 绿苍蝇 / 灰苍蝇 | ARoachIFoundOnMyPillow | https://opengameart.org/content/16x16-flies | 每种 3 帧飞行动画 | CC0 |
| 胡蜂 | Nerveona | https://opengameart.org/content/flying-hornetwasp | 2 帧高速振翅 | CC0 |
| 小鸟 | rmazanek | https://opengameart.org/content/bird-2 | 11×8 动画表，FACM 使用 Fly/Flap 行 | CC0 |

## 运行方式

- FACM 不要求用户登录 OpenGameArt。
- 第一次选择对应宠物时，FACM 直接下载公开素材文件并缓存到运行目录的 `animal-sprites`。
- 后续运行优先使用本地缓存。
- 桌宠运行由 FACM 自己的 `SpritePetWindow` 完成，不依赖 Canva、Desktop Homunculus、Vulkan 或外部 3D 引擎。
- 普通猫狗使用横向逐帧动画；蜘蛛、蚂蚁根据移动方向切换 Sprite Sheet 的方向行；飞虫使用更短的方向决策周期和高速动画帧率。

## 质量门槛

Windows CI 的 `--animal-pet-test` 会：

1. 下载并解码每一套当前内置 Sprite Sheet；
2. 检查网格尺寸可整除；
3. 要求每个宠物至少两个不同动画帧；
4. 要求蜘蛛、蚂蚁的不同方向行渲染结果确实不同；
5. 检查透明窗口四角；
6. 验证“宠物复位”能够回到主屏幕工作区。
