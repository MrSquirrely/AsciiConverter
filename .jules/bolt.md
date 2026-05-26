## 2024-05-24 - OpenCVSharp Pixel Access Optimization
**Learning:** Using `Mat.At<T>()` in nested loops is significantly slower in OpenCvSharp due to method call overhead per pixel. Floating-point math for simple character mappings based on byte values is also a major bottleneck when executed per-pixel in a video frame loop.
**Action:** Next time, pre-compute lookup tables for byte-to-character mappings (O(1) array lookup instead of O(N) floating-point calculations) and use `GetUnsafeGenericIndexer<T>()` or direct memory access (`Mat.Data`) for iterative pixel operations.
