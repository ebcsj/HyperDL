# Dynamic segmentation status

Dynamic segmentation is not implemented. Current jobs use a fixed range layout
created before workers start; that layout is persisted and restored by SQLite.

Do not claim that a slow segment is split during a download. A future
implementation must atomically persist range changes, maintain inclusive byte
boundaries, and prove no gaps, overlaps, or duplicate progress before it can be
enabled.
