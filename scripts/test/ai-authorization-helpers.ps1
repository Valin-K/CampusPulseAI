function ConvertTo-Base64Url([byte[]]$Bytes) {
	return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-CampusPulseTestHeaders([bool]$IncludeScope = $true) {
	$signingKey = $env:CAMPUSPULSE_SIGNING_KEY
	if ([string]::IsNullOrWhiteSpace($signingKey) -or [Text.Encoding]::UTF8.GetByteCount($signingKey) -lt 32) {
		throw 'Set CAMPUSPULSE_SIGNING_KEY to match the isolated Compose stack; it must contain at least 32 bytes.'
	}

	$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
	$claims = @{
		iss = 'CampusPulse'; aud = 'CampusPulse.Api'; sub = '4'; user_id = '4'
		role = 'Student'; nbf = $now; exp = $now + 600
	}
	if ($IncludeScope) { $claims.scope = 'CampusPulse.Access' }
	$header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
	$payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($claims | ConvertTo-Json -Compress)))
	$unsigned = "$header.$payload"
	$hmac = New-Object Security.Cryptography.HMACSHA256
	try {
		$hmac.Key = [Text.Encoding]::UTF8.GetBytes($signingKey)
		$signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned)))
	}
	finally { $hmac.Dispose() }
	return @{ Authorization = "Bearer $unsigned.$signature" }
}
