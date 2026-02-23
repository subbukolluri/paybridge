Write-Host "Sending test payments..."
for ($i = 1; $i -le 200; $i++) {
    $body = @{
        merchantId     = "MERCHANT-001"
        idempotencyKey = [guid]::NewGuid().ToString()
        amount         = 50 + $i * 10
        currency       = "USD"
        customerEmail  = "test@example.com"
        method         = 0  # CreditCard
    } | ConvertTo-Json

    try {
        $r = Invoke-RestMethod -Uri http://localhost:5000/api/payments -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        Write-Host "[$i] OK: $($r.status)"
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        Write-Host "[$i] HTTP $code"
    }
    Start-Sleep -Milliseconds 300
}
Write-Host "`nDone! Refresh Grafana at http://localhost:3000"
