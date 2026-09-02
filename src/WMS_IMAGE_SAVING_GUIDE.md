# WMS GetMap Image Saving Guide

## Overview

This guide explains how to capture and save WMS GetMap image responses from the workflow execution to the local temp folder for inspection and debugging.

## Architecture Changes

### 1. Enhanced Binary Response Handling

The `WorkflowRunner.ExecuteHttpAsync` method has been enhanced to:

- **Detect binary content** by examining the Content-Type header
  - Recognizes: `image/*`, `application/pdf`, `application/octet-stream`

- **Preserve binary data** by:
  - Reading response as byte array instead of string
  - Converting to base64-encoded string for safe JSON storage
  - Storing metadata (encoding type, size, content-type)

- **Maintain backward compatibility** by:
  - Continuing to parse text-based responses (JSON, XML, HTML) as before
  - Only applying binary handling to detected binary content types

### 2. Response Structure

Binary HTTP responses are now returned with the following structure:

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "responseTimeMs": 1234,
  "headers": { /* all response headers */ },
  "body": {
	"data": "iVBORw0KGgoAAAANSUhEUgAA...",  // Base64-encoded binary data
	"metadata": {
	  "encoding": "base64",
	  "size": 8192,
	  "contentType": "image/png"
	}
  }
}
```

## Usage

### Helper Method: SaveBinaryResponseToTempFile

The test suite includes a helper method to extract and save binary response data:

```csharp
public static string SaveBinaryResponseToTempFile(JToken httpResponse, string fileExtension = ".png")
```

**Parameters:**
- `httpResponse`: The HTTP response JToken from workflow execution (contains base64-encoded data)
- `fileExtension`: File extension (default: ".png")

**Returns:** Full path to the saved file

**Example:**

```csharp
// After workflow execution captures the GetMap response
var imagePath = SaveBinaryResponseToTempFile(workflowResponse["steps"]["wms-getmap"]["output"]);
Console.WriteLine($"Image saved to: {imagePath}");
```

### Integration Test

A new integration test demonstrates the complete workflow:

```csharp
[Fact(Skip = "Requires live WMS server access - enable for integration testing")]
public async Task WmsGetMapWorkflowExecutesAndSavesImageToTempFolder()
{
	// ... test implementation
}
```

To enable this test for integration testing:
1. Remove the `Skip` attribute
2. Ensure network access to the WMS server (https://ms.web-gis.ch)
3. Run the test with `dotnet test`

## Temp Folder Location

Images are saved to:
```
{System.IO.Path.GetTempPath()}/octave-workflow-images/
```

On Windows, this typically resolves to:
```
C:\Users\{UserName}\AppData\Local\Temp\octave-workflow-images\
```

On Linux/Mac:
```
/tmp/octave-workflow-images/
```

## Filename Convention

Files are named with timestamp precision:
```
wms-response-{yyyyMMdd-HHmmss-fff}{extension}
```

Example: `wms-response-20250401-143052-789.png`

## Workflow Configuration

The WMS GetMap workflow (dddddddd-1111-1111-1111-11111111111d) performs:

1. **GetCapabilities** - Fetches WMS service capabilities (XML response)
2. **Extract Service Info** - Parses capabilities to extract layer information
3. **Check Layer Exists** - Condition step validates layer presence
4. **Generate Random Extent** - Creates random EPSG:2056 (Swiss LV95) coordinates
5. **GetMap** - Requests map image with random extent (PNG binary response)
6. **Extract Map Info** - Captures response metadata
7. **Log Layer Error** (Else branch) - Error handling if layer not found

## Example: Full Workflow with Image Saving

```csharp
// 1. Set up workflow and input
var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
var input = new JObject();

// 2. Execute workflow via runner
var run = await workflowRunner.RunAsync(Guid.NewGuid(), workflow, input, cancellationToken);

// 3. Extract and save image
if (run.Context?["steps"]?["wms-getmap"] is JObject getMapStep)
{
	var output = getMapStep["output"];
	try
	{
		var imagePath = SaveBinaryResponseToTempFile(output, ".png");
		Console.WriteLine($"✓ GetMap image saved: {imagePath}");
		Console.WriteLine($"✓ File size: {new FileInfo(imagePath).Length} bytes");
	}
	catch (Exception ex)
	{
		Console.WriteLine($"✗ Failed to save image: {ex.Message}");
	}
}

// 4. Inspect and process the saved image
var imageInfo = new FileInfo(imagePath);
Console.WriteLine($"  Created: {imageInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"  Size: {imageInfo.Length} bytes");
```

## Debugging Binary Responses

### Verify Image Data

```csharp
// Check if image data exists and is valid
var body = httpResponse["body"];
if (body is JObject bodyObj && bodyObj["data"] is JToken dataToken)
{
	var base64Data = dataToken.Value<string>();
	var imageBytes = Convert.FromBase64String(base64Data);
	Console.WriteLine($"Image data valid: {imageBytes.Length} bytes");
}
```

### Common Issues

1. **Empty Image Data**
   - Verify the GetMap HTTP request succeeded (statusCode == 200)
   - Check that the layer name was correctly extracted
   - Confirm EPSG:2056 extent is valid for the requested layer

2. **Invalid Base64 Data**
   - Ensure the response body contains a "data" field with base64-encoded content
   - Verify the metadata encoding is "base64"

3. **File Not Created**
   - Check temp folder permissions: `Path.Combine(Path.GetTempPath(), "octave-workflow-images")`
   - Ensure sufficient disk space
   - Verify file extension parameter is valid

## Performance Considerations

- **Memory**: Binary data is kept in memory as base64 string during workflow execution
  - A 1MB PNG becomes ~1.3MB in base64 format
  - Large images may impact memory usage

- **Network**: Binary responses increase download time
  - No compression applied to base64-encoded strings
  - Consider setting appropriate timeouts for large images

- **Disk**: Images are only written when explicitly saved
  - Use the helper method to control when/where images are persisted
  - Clean up old images periodically from the temp folder

## Migration Guide

### For Existing Code Reading HTTP Responses

**Before** (text-only):
```csharp
var body = httpResponse["body"].Value<string>();
```

**After** (handles both text and binary):
```csharp
var body = httpResponse["body"];
if (body is JObject bodyObj && bodyObj["data"] != null)
{
	// Binary content (base64-encoded)
	var base64Data = bodyObj["data"].Value<string>();
	var imageBytes = Convert.FromBase64String(base64Data);
}
else if (body is JValue)
{
	// Text content (JSON/XML/HTML)
	var textData = body.Value<string>();
}
```

## Testing

Run the image saving test suite:

```powershell
# Run all WMS tests
dotnet test --filter "WmsGetMapWorkflowTests"

# Enable integration test (requires live server)
# Remove Skip attribute from WmsGetMapWorkflowExecutesAndSavesImageToTempFolder
# Then run:
dotnet test --filter "WmsGetMapWorkflowExecutesAndSavesImageToTempFolder"
```

## Related Files

- **WorkflowRunner.cs**: Enhanced ExecuteHttpAsync method
- **WmsGetMapWorkflowTests.cs**: Helper methods and integration test
- **dddddddd-1111-1111-1111-11111111111d.json**: Workflow definition with GetMap step
- **skillset.md**: Workflow model documentation
