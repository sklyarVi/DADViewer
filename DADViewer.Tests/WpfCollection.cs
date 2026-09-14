namespace DADViewer.Tests;

// WPF pack resources are process-wide and cannot be loaded concurrently by separate dispatchers.
[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection;
