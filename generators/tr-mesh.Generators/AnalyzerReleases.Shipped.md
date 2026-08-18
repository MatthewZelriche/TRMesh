## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TRMSOA001 | TRMesh.SourceGeneration | Error | SoA fields must be accessible to the generated type.
TRMSOA002 | TRMesh.SourceGeneration | Error | SoA fields must use a supported unmanaged value type.
TRMSOA003 | TRMesh.SourceGeneration | Error | Generic SoA structs are not supported.
TRMSOA004 | TRMesh.SourceGeneration | Error | SoA structs must declare at least one instance field.
TRMSOA005 | TRMesh.SourceGeneration | Error | SoA targets must be supported top-level classes.
TRMSOA006 | TRMesh.SourceGeneration | Error | SoA target classes must be partial.
TRMSOA007 | TRMesh.SourceGeneration | Error | SoA field names must be unambiguous within the generated hierarchy.
