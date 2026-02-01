---
name: install-ui-ux-pro-max
description: Install the ui-ux-pro-max skill from GitHub for Cursor. Use when the user wants to install ui-ux-pro-max skill, or when they mention installing UI/UX design intelligence tools.
---

# Install UI/UX Pro Max Skill

帮助安装 ui-ux-pro-max skill，这是一个提供 UI/UX 设计智能的 Cursor skill。

## 安装方法

### 方法 1: 使用 CLI 工具（推荐）

使用官方 CLI 工具 `uipro-cli` 安装：

```bash
# 1. 全局安装 CLI 工具
npm install -g uipro-cli

# 2. 进入项目目录
cd /path/to/your/project

# 3. 为 Cursor 安装 skill
uipro init --ai cursor
```

### 方法 2: 手动从 GitHub 安装

如果 CLI 工具不可用，可以手动从 GitHub 仓库安装：

```bash
# 1. 克隆仓库到临时目录
git clone https://github.com/nextlevelbuilder/ui-ux-pro-max-skill.git /tmp/ui-ux-pro-max-skill

# 2. 复制 skill 文件到 Cursor skills 目录
# Windows (PowerShell):
Copy-Item -Path "$env:USERPROFILE\.cursor\skills\ui-ux-pro-max" -Destination "$env:USERPROFILE\.cursor\skills\ui-ux-pro-max" -Recurse -Force
# 或者项目级别:
Copy-Item -Path ".cursor\skills\ui-ux-pro-max" -Destination ".cursor\skills\ui-ux-pro-max" -Recurse -Force

# macOS/Linux:
cp -r /tmp/ui-ux-pro-max-skill/.claude/skills/ui-ux-pro-max ~/.cursor/skills/ui-ux-pro-max
# 或者项目级别:
cp -r /tmp/ui-ux-pro-max-skill/.claude/skills/ui-ux-pro-max .cursor/skills/ui-ux-pro-max
```

### 方法 3: 使用 npx（如果支持）

如果用户提到使用 `npx skills add`，尝试：

```bash
npx skills add https://github.com/nextlevelbuilder/ui-ux-pro-max-skill --skill ui-ux-pro-max
```

## 安装步骤

当用户请求安装时，按以下步骤操作：

1. **检查是否已安装**
   - 检查 `~/.cursor/skills/ui-ux-pro-max/` 或 `.cursor/skills/ui-ux-pro-max/` 是否存在
   - 如果已存在，告知用户并询问是否要更新

2. **选择安装方法**
   - 优先使用 CLI 工具（方法 1）
   - 如果 npm 不可用，使用手动安装（方法 2）

3. **验证安装**
   - 检查 `SKILL.md` 文件是否存在
   - 检查 `scripts/` 目录是否存在
   - 检查 Python 是否已安装（skill 需要 Python 3.x）

4. **安装依赖**
   - 确保 Python 3.x 已安装
   - 如果需要，安装 Python 依赖包（通常不需要额外依赖）

## 安装位置

- **个人 skill**: `~/.cursor/skills/ui-ux-pro-max/`（所有项目可用）
- **项目 skill**: `.cursor/skills/ui-ux-pro-max/`（仅当前项目可用）

推荐使用项目级别安装，这样 skill 可以随项目一起版本控制。

## 验证安装成功

安装完成后，检查以下文件是否存在：

- `SKILL.md` - 主 skill 文件
- `scripts/search.py` - 搜索脚本
- `data/` - 数据文件目录

## 使用说明

安装成功后，skill 会自动激活。当用户请求 UI/UX 相关工作时，skill 会提供：
- 67 种 UI 风格推荐
- 96 种配色方案
- 57 种字体配对
- 25 种图表类型
- 13 种技术栈支持

## 故障排除

### Python 未安装
```bash
# Windows
winget install Python.Python.3.12

# macOS
brew install python3

# Ubuntu/Debian
sudo apt update && sudo apt install python3
```

### npm 未安装
```bash
# 安装 Node.js (包含 npm)
# Windows: 从 nodejs.org 下载安装程序
# macOS: brew install node
# Ubuntu/Debian: sudo apt install nodejs npm
```

### 权限问题
确保对目标目录有写入权限：
- Windows: 以管理员身份运行 PowerShell
- macOS/Linux: 使用 `sudo` 或确保目录权限正确
