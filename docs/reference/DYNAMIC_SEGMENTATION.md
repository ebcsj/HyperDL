# Dynamic segmentation status

Dynamic segmentation is implemented using cooperative splitting. For eligible
downloads (range-supported servers, known file size, ≥ 64 MiB), HyperDL creates
an initial set of ranges and allows idle workers to split large in-flight
segments when safe to do so. The scheduler tracks segments in memory and
persists new segments to SQLite with UPSERTs, preserving coverage (no gaps or
overlaps). Dynamic segmentation can be disabled in Settings; the default is
enabled for eligible downloads. Minimum tail size, maximum segment count, and
the global 64 MiB network-buffer budget are enforced by the engine.
