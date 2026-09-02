# How to Run the Workflow `cccccccc-0000-0000-0000-00000000000c` in the Web API

This guide explains how to trigger and monitor the **WMS GetCapabilities workflow** using the Octave Workflow Web API.

---

## Quick Start

### 1. Start the Web API

Open PowerShell and navigate to the API project directory:

```powershell
cd C:\Users\webadmin.bad-zbook\Downloads\Octave.Workflow.Net10\Octave.Workflow\src\Octave.Workflow.Api
dotnet run
```

The API will typically start on:
- **HTTP**: `http://localhost:5000`
- **HTTPS**: `https://localhost:5001`

You'll see output like:
```
info: Microsoft.Hosting.Lifetime[14]
	  Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[15]
	  Application started. Press Ctrl+C to exit.
```

---

## Workflow Overview

**Workflow ID:** `cccccccc-0000-0000-0000-00000000000c`

**Name:** WMS — List Available Layers

**Purpose:** Fetches WMS (Web Map Service) capabilities metadata from a remote server and extracts layer information.

**What it does:**
1. Makes a GET request to a WMS GetCapabilities endpoint (returns XML)
2. Automatically converts XML response to JSON
3. Extracts service metadata: title, version, max dimensions, layers, CRS info

---

## Running the Workflow via REST API

### Method 1: Using PowerShell (Recommended for Testing)

#### Step 1: Trigger the workflow

```powershell
$workflowId = "cccccccc-0000-0000-0000-00000000000c"
$apiUrl = "https://localhost:5001"

# Ignore SSL certificate issues (for local development only)
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

$response = Invoke-WebRequest `
	-Uri "$apiUrl/api/workflows/$workflowId/runs" `
	-Method POST `
	-Headers @{"Content-Type" = "application/json"} `
	-Body '{}' `
	-SkipCertificateCheck

$runData = $response.Content | ConvertFrom-Json
$runId = $runData.runId

Write-Host "Workflow triggered successfully!"
Write-Host "Run ID: $runId"
Write-Host "Status: $($runData.status)"
```

**Response (202 Accepted):**
```json
{
  "runId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "workflowId": "cccccccc-0000-0000-0000-00000000000c",
  "status": "queued"
}
```

#### Step 2: Check the workflow execution status

```powershell
$runId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"  # From previous response
$apiUrl = "https://localhost:5001"

[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

$response = Invoke-WebRequest `
	-Uri "$apiUrl/api/runs/$runId" `
	-Method GET `
	-SkipCertificateCheck

$runData = $response.Content | ConvertFrom-Json

Write-Host "Run Status: $($runData.status)"
Write-Host "Started: $($runData.startedUtc)"
Write-Host "Completed: $($runData.completedUtc)"

# Pretty-print the extracted data (if completed)
if ($runData.status -eq "Succeeded") {
	$context = $runData.context | ConvertTo-Json -Depth 10
	Write-Host "Extracted Metadata:`n$context"
}
```

**Response (200 OK) when completed:**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "workflowId": "cccccccc-0000-0000-0000-00000000000c",
  "status": "Succeeded",
  "startedUtc": "2025-04-15T10:30:00Z",
  "completedUtc": "2025-04-15T10:30:05Z",
  "input": {},
  "context": {
	"serviceTitle": "Geodata Portal",
	"wmsVersion": "1.3.0",
	"maxWidth": "4096",
	"maxHeight": "4096",
	"onlineResource": "https://ms.web-gis.ch/api/Ogc/...",
	"rootLayerTitle": "Root Layer",
	"layerName": "layer_0",
	"layerTitle": "Available Layers",
	"layerCrs": "EPSG:4326"
  },
  "steps": [
	{
	  "stepId": "get-capabilities",
	  "status": "Succeeded",
	  "startedUtc": "2025-04-15T10:30:00Z",
	  "completedUtc": "2025-04-15T10:30:02Z",
	  "output": {
		"statusCode": 200,
		"headers": { "Content-Type": "text/xml" },
		"body": { /* WMS XML converted to JSON */ }
	  }
	},
	{
	  "stepId": "extract-service-info",
	  "status": "Succeeded",
	  "startedUtc": "2025-04-15T10:30:02Z",
	  "completedUtc": "2025-04-15T10:30:05Z",
	  "output": null
	}
  ],
  "errorCode": null,
  "error": null
}
```

---

### Method 2: Using cURL

#### Trigger the workflow:

```bash
curl -X POST https://localhost:5001/api/workflows/cccccccc-0000-0000-0000-00000000000c/runs \
  -H "Content-Type: application/json" \
  -d '{}' \
  -k  # Skip SSL verification (local dev only)
```

#### Check status:

```bash
curl -X GET https://localhost:5001/api/runs/{runId} \
  -H "Content-Type: application/json" \
  -k  # Skip SSL verification (local dev only)
```

---

### Method 3: Using Visual Studio Code REST Client Extension

Create a file `requests.http` in your project root:

```http
### Get workflow details
GET https://localhost:5001/api/workflows/cccccccc-0000-0000-0000-00000000000c HTTP/1.1
Content-Type: application/json

### Trigger the workflow
POST https://localhost:5001/api/workflows/cccccccc-0000-0000-0000-00000000000c/runs HTTP/1.1
Content-Type: application/json

{}

### Get run status (replace {runId} with actual ID from trigger response)
GET https://localhost:5001/api/runs/{runId} HTTP/1.1
Content-Type: application/json
```

Then use the "Send Request" button in VS Code to execute each request.

---

### Method 4: Using Postman

1. **Create a new POST request:**
   - URL: `https://localhost:5001/api/workflows/cccccccc-0000-0000-0000-00000000000c/runs`
   - Method: `POST`
   - Headers: `Content-Type: application/json`
   - Body (raw JSON):
	 ```json
	 {}
	 ```

2. **Send** — You'll receive a 202 response with a `runId`

3. **Create a new GET request to check status:**
   - URL: `https://localhost:5001/api/runs/{runId}`
   - Replace `{runId}` with the ID from step 2

4. **Send** — Repeat until status is `"Succeeded"` or `"Failed"`

---

## Workflow Structure

The workflow consists of 2 steps:

### Step 1: GET WMS GetCapabilities (HTTP Request)
- **Type**: `http`
- **Method**: GET
- **URL**: `https://ms.web-gis.ch/api/Ogc/e5a071f2-ffce-4d18-9f67-dfd8052cd7c9?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities`
- **Headers**: `Accept: text/xml`
- **Timeout**: 30 seconds
- **Output**: HTTP response with XML body (auto-converted to JSON)

### Step 2: Extract Service Metadata (Script Step)
- **Type**: `script`
- **Assigns**: Extracts 8 pieces of metadata using JSONPath expressions:
  - `serviceTitle`
  - `wmsVersion`
  - `maxWidth` / `maxHeight`
  - `onlineResource`
  - `rootLayerTitle`
  - `layerName` / `layerTitle`
  - `layerCrs`

---

## Understanding the Response

### When the workflow completes successfully (`status: "Succeeded"`):

The `context` object contains the extracted data:
```json
{
  "serviceTitle": "Geodata Portal",
  "wmsVersion": "1.3.0",
  "maxWidth": "4096",
  "maxHeight": "4096",
  "onlineResource": "https://ms.web-gis.ch/...",
  "rootLayerTitle": "Root Layer",
  "layerName": "layer_0",
  "layerTitle": "Available Layers",
  "layerCrs": "EPSG:4326"
}
```

### The `steps` array contains execution details for each step:

```json
{
  "stepId": "get-capabilities",
  "status": "Succeeded",
  "startedUtc": "2025-04-15T10:30:00Z",
  "completedUtc": "2025-04-15T10:30:02Z",
  "output": {
	"statusCode": 200,
	"isSuccess": true,
	"headers": { /* HTTP headers */ },
	"body": { /* XML converted to JSON */ },
	"certificate": { /* SSL certificate details */ }
  }
}
```

---

## Troubleshooting

### Issue: `"status": 404`
**Solution:** The workflow ID is incorrect or the workflow file doesn't exist.
- Verify the file exists at: `src/Octave.Workflow.Api/data/workflows/cccccccc-0000-0000-0000-00000000000c.json`

### Issue: `"status": 500` or `"error": "Host not allowed"`
**Solution:** The remote host `ms.web-gis.ch` is not in the allowed list.
- Add it to `appsettings.json`:
  ```json
  "WorkflowSecurity": {
	"AllowedHosts": [
	  "ms.web-gis.ch",
	  "api.example.com"
	],
	"AllowHttp": false
  }
  ```

### Issue: Workflow stays in `"status": "Running"` for too long
**Solution:** The remote API may be slow or unreachable.
- Check the step output's `statusCode` — if it's not 200, the HTTP request failed
- Verify network connectivity to `ms.web-gis.ch`
- Increase the timeout (default is 30 seconds)

### Issue: SSL Certificate Validation Error
**Solution:** For local development, skip certificate verification:
- PowerShell: Use `-SkipCertificateCheck` flag
- cURL: Use `-k` flag
- Postman: Disable SSL verification in Settings → General

---

## Full Example: Complete Workflow Run in PowerShell

```powershell
# Configuration
$workflowId = "cccccccc-0000-0000-0000-00000000000c"
$apiUrl = "https://localhost:5001"
$maxWaitSeconds = 60

# Ignore SSL cert for local dev
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# Step 1: Trigger the workflow
Write-Host "Triggering workflow $workflowId..."
$triggerResponse = Invoke-WebRequest `
	-Uri "$apiUrl/api/workflows/$workflowId/runs" `
	-Method POST `
	-Headers @{"Content-Type" = "application/json"} `
	-Body '{}' `
	-SkipCertificateCheck

$runData = $triggerResponse.Content | ConvertFrom-Json
$runId = $runData.runId
Write-Host "✓ Workflow queued. Run ID: $runId" -ForegroundColor Green

# Step 2: Poll for completion
$startTime = Get-Date
$completed = $false
$maxWaitTime = [TimeSpan]::FromSeconds($maxWaitSeconds)

while (-not $completed) {
	$statusResponse = Invoke-WebRequest `
		-Uri "$apiUrl/api/runs/$runId" `
		-Method GET `
		-SkipCertificateCheck

	$run = $statusResponse.Content | ConvertFrom-Json
	$status = $run.status

	Write-Host "Status: $status (elapsed: $([DateTime]::UtcNow - $startTime.ToUniversalTime() | % TotalSeconds)s)"

	if ($status -in @("Succeeded", "Failed", "Skipped")) {
		$completed = $true
		Write-Host "`n✓ Workflow completed with status: $status" -ForegroundColor Green
	}
	elseif (([DateTime]::UtcNow - $startTime.ToUniversalTime()) -gt $maxWaitTime) {
		Write-Host "✗ Timeout waiting for workflow to complete" -ForegroundColor Red
		exit 1
	}
	else {
		Start-Sleep -Seconds 2
	}
}

# Step 3: Display results
Write-Host "`n--- WORKFLOW RESULTS ---"
Write-Host "Started: $($run.startedUtc)"
Write-Host "Completed: $($run.completedUtc)"

if ($run.status -eq "Succeeded") {
	Write-Host "`nExtracted Metadata:"
	$run.context | ConvertTo-Json | Write-Host
} else {
	Write-Host "Error: $($run.error)" -ForegroundColor Red
}
```

---

## Related Documentation

- **skillset.md** — Complete workflow model reference
- **README.md** — Project overview
- **API Endpoints** (in this guide) — Full REST API documentation
- **Workflow Definition** — `src/Octave.Workflow.Api/data/workflows/cccccccc-0000-0000-0000-00000000000c.json`

