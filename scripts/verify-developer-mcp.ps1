param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [switch]$TrailingSlash
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(20)
$endpoint = $BaseUrl.TrimEnd('/') + '/mcp'
if ($TrailingSlash) { $endpoint += '/' }
$script:requestId = 0
function Convert-ProtocolJson([string]$Json) {
    try { return ($Json | ConvertFrom-Json) }
    catch { throw 'Response contains invalid JSON' }
}
function Invoke-Mcp([string]$Method, $Parameters, [string]$Version) {
    $script:requestId++
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $endpoint)
    $request.Headers.TryAddWithoutValidation('Accept', 'application/json, text/event-stream') | Out-Null
    $request.Headers.TryAddWithoutValidation('MCP-Protocol-Version', $Version) | Out-Null
    if ($Version -eq '2026-07-28') {
        $request.Headers.TryAddWithoutValidation('Mcp-Method', $Method) | Out-Null
        $Parameters['_meta'] = @{ 'io.modelcontextprotocol/protocolVersion' = $Version; 'io.modelcontextprotocol/clientCapabilities' = @{} }
    }
    $request.Headers.TryAddWithoutValidation('Origin', $BaseUrl.TrimEnd('/')) | Out-Null
    $payload = @{ jsonrpc = '2.0'; id = $script:requestId; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 12 -Compress
    $request.Content = New-Object System.Net.Http.StringContent($payload, [Text.Encoding]::UTF8, 'application/json')
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode) from MCP endpoint" }
            $media = $response.Content.Headers.ContentType.MediaType
            if ($media -eq 'application/json') { $message = Convert-ProtocolJson $body }
            elseif ($media -eq 'text/event-stream') {
                $message = $null
                foreach ($event in ($body -split '\r?\n\r?\n')) {
                    $data = @($event -split '\r?\n' | Where-Object { $_ -match '^data:' } | ForEach-Object { $_ -replace '^data: ?', '' }) -join "`n"
                    if ($data) {
                        $candidate = Convert-ProtocolJson $data
                        if ($candidate.id -eq $script:requestId) { $message = $candidate; break }
                    }
                }
                if (-not $message) { throw 'SSE response has no matching JSON-RPC message' }
            } else { throw "Unexpected content type '$media'; check proxy routing and SPA fallback" }
            if ($message.jsonrpc -ne '2.0' -or $message.id -ne $script:requestId) { throw 'Invalid JSON-RPC response' }
            if ($message.error) { throw "MCP protocol error $($message.error.code)" }
            if (-not $message.result) { throw 'MCP response has no result' }
            return $message.result
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Get-PublicDocs([string]$Path) {
    $response = $client.GetAsync($BaseUrl.TrimEnd('/') + $Path).GetAwaiter().GetResult()
    try {
        if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode) from public documentation endpoint" }
        if ($response.Content.Headers.ContentType.MediaType -ne 'application/json') { throw 'Public documentation endpoint returned a non-JSON response' }
        return (Convert-ProtocolJson $response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    } finally { $response.Dispose() }
}
try {
    $initialized = Invoke-Mcp 'initialize' @{ protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'keygate-deployment-probe'; version = '1.0' } } '2025-11-25'
    if ($initialized.protocolVersion -ne '2025-11-25') { throw 'Unexpected negotiated legacy protocol version' }
    $tools = Invoke-Mcp 'tools/list' @{} $initialized.protocolVersion
    $expected = @('search_integration_docs', 'get_integration_doc', 'get_api_reference', 'get_code_example', 'get_troubleshooting_guide')
    $names = @($tools.tools | ForEach-Object { $_.name })
    if ($names.Count -ne 5 -or @(Compare-Object ($expected | Sort-Object) ($names | Sort-Object)).Count) { throw 'Expected exactly the five public documentation tools' }
    $resources = Invoke-Mcp 'resources/list' @{} $initialized.protocolVersion
    $documentIds = @('quick-start', 'activation', 'trial', 'heartbeat', 'logout', 'device-release', 'versions', 'offline', 'errors')
    $expectedUris = @($documentIds | ForEach-Object { 'keygate://docs/' + $_ })
    $resourceUris = @($resources.resources | ForEach-Object { $_.uri })
    if ($resourceUris.Count -ne 9 -or @($resourceUris | Sort-Object -Unique).Count -ne 9 -or @(Compare-Object ($expectedUris | Sort-Object) ($resourceUris | Sort-Object) -CaseSensitive).Count) { throw 'Expected exactly the nine unique public documentation resource URIs' }
    $index = @(Get-PublicDocs '/api/integrations/developer-docs')
    $activation = Get-PublicDocs '/api/integrations/developer-docs/activation'
    $resource = Invoke-Mcp 'resources/read' @{ uri = 'keygate://docs/activation' } $initialized.protocolVersion
    $tool = Invoke-Mcp 'tools/call' @{ name = 'get_integration_doc'; arguments = @{ docId = 'activation' } } $initialized.protocolVersion
    if ($tool.isError) { throw 'Public document tool returned an error' }
    $document = Convert-ProtocolJson $tool.content[0].text
    $indexIds = @($index | ForEach-Object { $_.id })
    if ($index.Count -ne 9 -or @($indexIds | Sort-Object -Unique).Count -ne 9 -or @(Compare-Object ($documentIds | Sort-Object) ($indexIds | Sort-Object) -CaseSensitive).Count -or $activation.markdown -cne $resource.contents[0].text -or $activation.markdown -cne $document.markdown -or $activation.version -ne $document.version -or @($index | Where-Object { $_.id -eq 'activation' -and $_.version -eq $activation.version }).Count -ne 1) { throw 'Site HTTP documentation and MCP Markdown/version do not match' }
    $discovery = Invoke-Mcp 'server/discover' @{} '2026-07-28'
    if ($discovery.supportedVersions -notcontains '2026-07-28' -or -not $discovery.capabilities.tools -or -not $discovery.capabilities.resources) { throw 'Current protocol discovery capabilities missing' }
    $currentTools = Invoke-Mcp 'tools/list' @{} '2026-07-28'
    $currentNames = @($currentTools.tools | ForEach-Object { $_.name })
    if ($currentNames.Count -ne 5 -or @(Compare-Object ($expected | Sort-Object) ($currentNames | Sort-Object)).Count) { throw 'Current protocol tools mismatch' }
    Write-Output 'PASS: legacy initialization, current discovery, JSON/SSE protocol, five tools, nine resources and site HTTP/MCP Markdown/version parity.'
    exit 0
} catch {
    Write-Error ("MCP verification failed: " + $_.Exception.Message) -ErrorAction Continue
    exit 1
} finally { $client.Dispose() }
