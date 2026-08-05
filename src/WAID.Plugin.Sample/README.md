# WAID sample plugin

This project is the SDK v2 conformance sample. `SamplePlugin` declares one `Scanner` capability and the minimum `EnvironmentRead` permission, then registers `EnvironmentScanner` through `IPluginServiceRegistry`. The scanner performs a real, read-only PATH availability check and never emits simulated results.

Build the solution, publish/copy the output assembly beside `WAID.Plugin.Sample.waid-plugin.json`, calculate SHA-256 for distribution, and install the manifest through the WAID Plugin Manager. Production packages should be Authenticode-signed whenever organization policy requires it.