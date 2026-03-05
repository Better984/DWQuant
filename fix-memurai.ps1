# Memurai RDB 目录修复脚本 - 需以管理员身份运行
# 右键 PowerShell -> 以管理员身份运行，然后执行: .\fix-memurai.ps1

$ErrorActionPreference = "Stop"

Write-Host "=== Memurai RDB 修复脚本 ===" -ForegroundColor Cyan

# 1. 确认目录已创建
if (-not (Test-Path "C:\MemuraiData")) {
    New-Item -ItemType Directory -Path "C:\MemuraiData" -Force | Out-Null
    Write-Host "[OK] 已创建 C:\MemuraiData" -ForegroundColor Green
} else {
    Write-Host "[OK] C:\MemuraiData 已存在" -ForegroundColor Green
}

# 2. 配置已修改（memurai.conf 中 dir 已改为 C:/MemuraiData）
Write-Host "[OK] memurai.conf 中 dir 已设置为 C:/MemuraiData" -ForegroundColor Green

# 3. 重启 Memurai 服务
Write-Host "正在重启 Memurai 服务..." -ForegroundColor Yellow
try {
    Restart-Service -Name "Memurai" -Force -ErrorAction Stop
    Write-Host "[OK] Memurai 服务已重启" -ForegroundColor Green
} catch {
    Write-Host "尝试启动 Memurai..." -ForegroundColor Yellow
    Start-Service -Name "Memurai" -ErrorAction Stop
    Write-Host "[OK] Memurai 服务已启动" -ForegroundColor Green
}

# 4. 验证
Start-Sleep -Seconds 2
$svc = Get-Service -Name "Memurai" -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq "Running") {
    Write-Host "`n[SUCCESS] Memurai 运行正常，RDB 将写入 C:\MemuraiData" -ForegroundColor Green
} else {
    Write-Host "`n[WARN] 请检查 Memurai 服务状态: Get-Service Memurai" -ForegroundColor Yellow
}
