# Unit Test Suite for WMS GetCapabilities Workflow

## Summary

A comprehensive unit test suite has been created for the **WMS GetCapabilities workflow** (`cccccccc-0000-0000-0000-00000000000c`). All 11 tests are passing and integrated seamlessly with the existing 47 tests.

## Test File Location

```
Octave.Workflow.UnitTests/WmsGetCapabilitiesWorkflowTests.cs
```

## Test Class

**Class Name:** `WmsGetCapabilitiesWorkflowTests`

**Namespace:** `Octave.Workflow.UnitTests`

## Workflow Under Test

| Property | Value |
|----------|-------|
| **ID** | `cccccccc-0000-0000-0000-00000000000c` |
| **Name** | WMS — List Available Layers |
| **Trigger** | Manual |
| **Steps** | 2 (HTTP request + Script extraction) |
| **Purpose** | Fetches WMS metadata and extracts layer information |

## Test Coverage

### 11 Unit Tests

#### 1. **WmsWorkflowCanBeLoadedFromDisk**
- Verifies the workflow JSON file exists on disk
- Confirms it can be deserialized successfully
- Validates core workflow properties (Id, Name, Enabled)

#### 2. **WmsWorkflowHasCorrectStepStructure**
- Verifies the workflow has exactly 2 steps
- Confirms step 1 is an `HttpRequestStep` with correct metadata
- Confirms step 2 is a `ScriptStep` with correct metadata

#### 3. **WmsHttpStepHasCorrectConfiguration**
- Validates HTTP request method (GET)
- Validates target URL and endpoint
- Validates headers (Accept: text/xml)
- Validates timeout (30 seconds)
- Confirms no request body for GET

#### 4. **WmsScriptStepExtractsAllMetadataFields**
- Verifies all 9 metadata fields are assigned:
  - serviceTitle
  - wmsVersion
  - maxWidth / maxHeight
  - onlineResource
  - rootLayerTitle
  - layerName / layerTitle
  - layerCrs

#### 5. **WmsScriptStepUsesCorrectJsonPathExpressions**
- Validates JSONPath expressions reference the HTTP step's output
- Confirms expressions correctly navigate XML-converted JSON structure:
  - `steps.get-capabilities.output.body.WMS_Capabilities.*`
  - Validates XML attribute access (e.g., `@version`, `@xlink:href`)

#### 6. **WmsWorkflowHasManualTrigger**
- Confirms trigger type is "Manual"
- Validates no webhook key or schedule

#### 7. **WmsWorkflowCanBeRoundTrippedThroughSerialization**
- Serializes workflow to JSON
- Deserializes back to object model
- Confirms all properties are preserved through the round-trip

#### 8. **WmsWorkflowStepIdsAreUnique**
- Validates all step IDs are unique
- Confirms no duplicate step IDs

#### 9. **WmsWorkflowDeserializesWithMixedCaseProperties**
- Tests deserialization with mixed-case property names
- Validates both "Id" (PascalCase) and "id" (camelCase) work correctly

#### 10. **WmsWorkflowMetadataIsCorrectlyStructured**
- Validates workflow metadata (timestamps, enabled status)
- Confirms timestamps are structured correctly
- Validates UpdatedUtc ≥ CreatedUtc

#### 11. **WmsHttpStepIsConfiguredForXmlResponse**
- Confirms Accept header is set to "text/xml"
- Validates step is ready for XML response parsing
- Confirms body type defaults to JSON (for auto-conversion)

## Test Execution

### Run All WMS Tests

```powershell
# Run just the WMS tests
dotnet test --filter "ClassName=Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests"

# Or from Visual Studio Test Explorer
# Search for: WmsGetCapabilitiesWorkflowTests
```

### Run All Tests (Including WMS)

```powershell
# Run full test suite
dotnet test Octave.Workflow.UnitTests/Octave.Workflow.UnitTests.csproj

# Expected: 58 Passed, 0 Failed
# - 11 WMS tests
# - 47 existing tests
```

## Test Results

✅ **All 11 WMS tests: PASSED**

```
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsHttpStepHasCorrectConfiguration Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsScriptStepUsesCorrectJsonPathExpressions Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowMetadataIsCorrectlyStructured Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowDeserializesWithMixedCaseProperties Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsScriptStepExtractsAllMetadataFields Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsHttpStepIsConfiguredForXmlResponse Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowHasManualTrigger Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowCanBeLoadedFromDisk Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowStepIdsAreUnique Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowHasCorrectStepStructure Passed
Octave.Workflow.UnitTests.WmsGetCapabilitiesWorkflowTests.WmsWorkflowCanBeRoundTrippedThroughSerialization Passed
```

**Full Test Suite: 58 Passed, 0 Failed** ✅

## Test Design Principles

### 1. **Independence**
- Tests embed the workflow JSON inline (from `GetWmsWorkflowJson()`)
- Tests don't depend on external file state
- Can run offline

### 2. **Clarity**
- Clear, descriptive test names
- Comprehensive XML documentation
- Arrange-Act-Assert (AAA) pattern

### 3. **Maintainability**
- Follows existing test patterns in the codebase
- Uses xUnit v3 best practices
- Consistent with WorkflowDeserializationIntegrationTests style

### 4. **Coverage**
- Structure validation (steps, types, IDs)
- Configuration validation (headers, timeout, URL)
- Serialization round-trip testing
- Metadata extraction validation
- Expression validation

## Key Testing Insights

### Workflow Structure
```
WorkflowDefinition (cccccccc-0000-0000-0000-00000000000c)
├── Step 1: HttpRequestStep (get-capabilities)
│   ├── Method: GET
│   ├── URL: https://ms.web-gis.ch/api/Ogc/...
│   ├── Headers: { "Accept": "text/xml" }
│   └── Timeout: 30 seconds
└── Step 2: ScriptStep (extract-service-info)
	└── Assigns: 9 metadata fields via JSONPath expressions
```

### JSON-to-Object Mapping
- The workflow file uses mixed-case properties (both PascalCase and camelCase)
- The `WorkflowStepConverter` handles polymorphic deserialization
- XML responses are auto-converted to JSON for JSONPath navigation

### Data Extraction Path
```
HTTP Response XML
	↓
Auto-converted to JSON
	↓
Stored in: steps.get-capabilities.output.body
	↓
Script Step extracts via JSONPath:
	↓
steps.get-capabilities.output.body.WMS_Capabilities.Service.Title
steps.get-capabilities.output.body.WMS_Capabilities.@version
steps.get-capabilities.output.body.WMS_Capabilities.Service.MaxWidth
... (7 more fields)
```

## Integration with Existing Tests

The WMS tests are fully integrated with the existing test suite:

| Category | Tests | Pass Rate |
|----------|-------|-----------|
| **WMS Workflow** | 11 | 100% ✅ |
| **Deserialization** | 22 | 100% ✅ |
| **JSON Validation** | 14 | 100% ✅ |
| **Integration** | 8 | 100% ✅ |
| **Template Engine** | 3 | 100% ✅ |
| **Total** | **58** | **100% ✅** |

## Files Modified/Created

- ✅ **Created:** `Octave.Workflow.UnitTests/WmsGetCapabilitiesWorkflowTests.cs` (380 lines)
- ✅ **No existing files modified**
- ✅ **All tests passing**
- ✅ **Full build successful**

## Next Steps

To further expand test coverage, you could add:

1. **Integration Tests** (API-level)
   - Test the workflow trigger endpoint
   - Mock the WMS server response
   - Verify the complete execution flow

2. **Performance Tests**
   - Benchmark the XML-to-JSON conversion
   - Validate metadata extraction performance

3. **Edge Case Tests**
   - Malformed XML responses
   - Missing WMS Capabilities elements
   - Network timeout simulation

## References

- **Workflow Definition:** `src/Octave.Workflow.Api/data/workflows/cccccccc-0000-0000-0000-00000000000c.json`
- **Model Code:** `src/Octave.Workflow.Application/Models/WorkflowModels.cs`
- **Test Patterns:** `src/Octave.Workflow.UnitTests/WorkflowDeserializationIntegrationTests.cs`
- **How to Run:** `HOW_TO_RUN_WORKFLOW_IN_WEBAPI.md`
- **Skillset:** `skillset.md` (Workflow Model section)

