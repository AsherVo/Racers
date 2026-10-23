// Toolbox is a process-wide singleton whose data caches aren't thread-safe, and the headless runner shares it,
// so every test runs in one collection, one at a time.
[assembly: CollectionBehavior( CollectionBehavior.CollectionPerAssembly )]
