# 重启 Memurai：先释放 6379 端口，再启动服务
# 需以管理员身份运行

# 1. 停止 Memurai 服务
Stop-Service -Name "Memurai" -Force -ErrorAction SilentlyContinue

# 2. 结束可能占用 6379 的 memurai 进程
Get-Process -Name "memurai" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 3. 启动 Memurai 服务
Start-Service -Name "Memurai" -ErrorAction Stop

Start-Sleep -Seconds 2
$svc = Get-Service -Name "Memurai"
if ($svc.Status -eq "Running") {
    Write-Host "Memurai started." -ForegroundColor Green
} else {
    Write-Host "Memurai failed to start. Check C:\MemuraiData\memurai-log.txt" -ForegroundColor Red
}
