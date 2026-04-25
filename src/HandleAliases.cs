// Project-wide type aliases for mesh slot handles. Prefer these names in public
// API and connectivity structs; generic storage still uses Handle<TTag>.

global using VertexHandle = TREditorSharp.Handle<TREditorSharp.VertexTag>;
global using HalfEdgeHandle = TREditorSharp.Handle<TREditorSharp.HalfEdgeTag>;
global using FaceHandle = TREditorSharp.Handle<TREditorSharp.FaceTag>;
