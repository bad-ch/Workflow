# Quick Reference: WMS GetMap Workflow

## Workflow IDs

| Workflow | ID | Purpose |
|----------|---|---------|
| **Original** | `cccccccc-0000-0000-0000-00000000000c` | List available layers |
| **New** | `dddddddd-1111-1111-1111-11111111111d` | Get map image with layer validation |

---

## Quick Start

### 1. Run Tests
```powershell
# Run WMS GetMap tests
dotnet test --filter "ClassName=Octave.Workflow.UnitTests.WmsGetMapWorkflowTests"

# Run all tests
dotnet test

# Expected: 70 Passed, 0 Failed
```

### 2. Trigger Workflow
```powershell
# Start the API
cd src/Octave.Workflow.Api
dotnet run

# In another terminal, trigger workflow (PowerShell)
$workflowId = "dddddddd-1111-1111-1111-11111111111d"
Invoke-WebRequest `
  -Uri "https://localhost:5001/api/workflows/$workflowId/runs" `
  -Method POST `
  -Headers @{"Content-Type" = "application/json"} `
  -Body '{}' `
  -SkipCertificateCheck
```

### 3. Check Results
```powershell
# Get run results (replace with actual run ID from above)
Invoke-WebRequest `
  -Uri "https://localhost:5001/api/runs/{runId}" `
  -Method GET `
  -SkipCertificateCheck | ConvertFrom-Json
```

---

## Workflow Structure

```
Workflow: WMS — Get Map with Layer Validation
├── Step 1: GET WMS GetCapabilities (HTTP)
├── Step 2: Extract service metadata (Script)
└── Step 3: Check if Layer Exists (Condition)
	├── THEN: Get Map Image
	│   ├── Step 3a: Generate Random Extent (Script)
	│   ├── Step 3b: WMS GetMap Request (HTTP)
	│   └── Step 3c: Extract Map Info (Script)
	└── ELSE: Handle Error
		└── Step 3d: Log Layer Error (Script)
```

---

## Test Coverage

### 12 Tests Included

| # | Test | Purpose |
|---|------|---------|
| 1 | `WmsGetMapWorkflowCanBeLoadedFromDisk` | Load workflow from file |
| 2 | `WmsGetMapWorkflowHasAllExpectedSteps` | Verify step structure |
| 3 | `WmsGetMapConditionStepHasCorrectBranches` | Verify Then/Else branches |
| 4 | `WmsGetMapConditionExpressionIsCorrect` | Validate condition logic |
| 5 | `WmsGetMapRandomExtentStepHasCorrectCoordinates` | Verify EPSG:2056 extent |
| 6 | `WmsGetMapHttpStepHasCorrectConfiguration` | Verify GetMap HTTP config |
| 7 | `WmsGetMapInfoExtractionStepCapturesResponseData` | Verify output extraction |
| 8 | `WmsGetMapElseBranchHandlesLayerNotFound` | Verify error handling |
| 9 | `WmsGetMapAllStepIdsAreUnique` | Validate unique IDs |
| 10 | `WmsGetMapWorkflowCanBeRoundTrippedThroughSerialization` | Test serialization |
| 11 | `WmsGetMapWorkflowMetadataIsCorrectlyStructured` | Verify metadata |
| 12 | **`WmsGetMapWorkflowAllStepsExecuteSuccessfully`** | **Main integration test** |

---

## Key Features

### ✅ Condition Step
- Validates layer exists in WMS response
- Checks HTTP 200 status
- Branches to GetMap or error handling

### ✅ Random Extent (EPSG:2056)
```json
{
  "minX": "2683000",
  "minY": "1247000", 
  "maxX": "2684000",
  "maxY": "1248000",
  "srs": "EPSG:2056",
  "width": "800",
  "height": "600",
  "format": "image/png"
}
```

### ✅ WMS GetMap Request
- Dynamic layer name from capabilities
- Dynamic extent from script
- PNG image format
- Proper Accept header

### ✅ Error Handling
- Logs error if layer not found
- Captures error context
- Graceful fallback path

---

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `dddddddd-1111-1111-1111-11111111111d.json` | 119 | Workflow definition |
| `WmsGetMapWorkflowTests.cs` | 528 | Unit test suite |
| `WMS_GETMAP_WORKFLOW_GUIDE.md` | Full documentation | |
| `WMS_WORKFLOW_UNIT_TESTS.md` | Original WMS tests | |
| `HOW_TO_RUN_WORKFLOW_IN_WEBAPI.md` | API usage guide | |
| `skillset.md` | Model reference | |

---

## Test Results

```
✅ 12/12 WMS GetMap Tests Passed
✅ 11/11 Original WMS Tests Passed
✅ 47/47 Other Tests Passed
─────────────────────────────────
✅ 70/70 Total Tests Passed
```

---

## Coordinate System Notes

**EPSG:2056 = Swiss LV95**
- Used in Switzerland
- Units: Meters
- Also called: LV95, Swiss Coordinates

**Sample Extents:**
- Zurich: 2683000, 1247000, 2684000, 1248000
- Bern: 2683000, 1247000, 2684000, 1248000  
- Geneva: 2492000, 1112000, 2493000, 1113000

To convert WGS84 (lat/lon) to EPSG:2056:
- Use Swiss Federal Statistical Office converter
- Or PostGIS: `ST_Transform(geom, 2056)`

---

## Next: How to Run It?

1. **Build:** `dotnet build`
2. **Test:** `dotnet test` (70 tests pass)
3. **Run:** Start API and trigger workflow
4. **Monitor:** Check status in real-time

See `HOW_TO_RUN_WORKFLOW_IN_WEBAPI.md` for detailed API instructions.

