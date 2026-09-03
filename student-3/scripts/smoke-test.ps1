param([string]$BaseUrl = "http://localhost:5083")
$ErrorActionPreference = "Stop"
function Assert-Status($response, $expected, $name) { if ($response.StatusCode -ne $expected) { throw "$name returned $($response.StatusCode), expected $expected" } Write-Host "PASS $name" }
Assert-Status (Invoke-WebRequest "$BaseUrl/health") 200 "backend health"
$rooms = Invoke-RestMethod "$BaseUrl/api/housing/rooms"
if ($rooms.Count -lt 1) { throw "No seeded dorm rooms returned" }; Write-Host "PASS seeded rooms"
$ticket = Invoke-WebRequest -Method Post -Uri "$BaseUrl/api/housing/tickets" -ContentType "application/json" -Body '{"StudentId":"smoke-test","Description":"broken heater in room 101"}'
Assert-Status $ticket 201 "maintenance create"
$created = $ticket.Content | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($created.Severity) -or [string]::IsNullOrWhiteSpace($created.RepairQueue)) { throw "AI classification fields were empty" }; Write-Host "PASS AI classification: $($created.Severity) / $($created.RepairQueue)"
$update = Invoke-WebRequest -Method Put -Uri "$BaseUrl/api/housing/tickets/$($created.Id)" -ContentType "application/json" -Body '{"Status":"Resolved"}'
Assert-Status $update 200 "maintenance resolve"
Write-Host "Residence smoke test completed successfully."
