using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Tests;

public class PipelineEngineConcurrencyTests
{
    [Fact]
    public async Task IngestResourceAsync_HandlesBurstLoad_WithBoundedProcessingConcurrency()
    {
        var storage = new FakeStorageProvider();
        var store = new InMemoryResourceStore();
        var index = new InMemorySearchIndex(store);
        var manager = new ResourceManager(store, index);
        var converter = new SlowCountingConverter(delayMilliseconds: 10);
        var resolver = new FixedResolver(converter);

        var engine = new PipelineEngine(
            storage,
            manager,
            resolver,
            new EmptyAiProvider(),
            new NoOpThumbnailProvider(),
            new NoOpCloudSyncProvider(),
            new ResourceRouter.Core.Services.NoOp.NoOpResourceHealthMonitor(),
            new ResourceRouter.Core.Services.NoOp.NoOpResourceFeatureExtractor(),
            new ResourceRouter.Core.Services.NoOp.NoOpResourceGovernanceProvider(),
            new ResourceRouter.Core.Services.NoOp.NoOpProcessingConfigurationProvider(),
            logger: null,
            maxConcurrentProcessing: 2,
            maxConcurrentSync: 1);

        var options = new ResourceIngestOptions
        {
            Privacy = PrivacyLevel.Private,
            SyncPolicy = SyncPolicy.LocalOnly,
            ProcessingModel = ModelType.None,
            Source = ResourceSource.Manual,
            PermissionPresetId = PermissionPreset.PrivatePresetId
        };

        var ingestTasks = Enumerable.Range(0, 50)
            .Select(i =>
            {
                var drop = new RawDropData
                {
                    Kind = RawDropKind.Text,
                    Text = "payload-" + i,
                    OriginalSuggestedName = "payload-" + i + ".txt"
                };

                return engine.IngestResourceAsync(
                    drop,
                    options,
                    waitingDuration: TimeSpan.FromMilliseconds(1));
            })
            .ToArray();

        await Task.WhenAll(ingestTasks);

        var allReady = await WaitUntilAsync(async () =>
        {
            var resources = await manager.ListRecentAsync(100);
            return resources.Count == 50 && resources.All(r => r.State == ResourceState.Ready);
        }, timeout: TimeSpan.FromSeconds(15));

        Assert.True(allReady, "Pipeline did not finish processing 50 resources within timeout.");
        Assert.True(
            converter.MaxObservedConcurrency <= 2,
            "Processing concurrency exceeded configured limit: " + converter.MaxObservedConcurrency);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private sealed class FakeStorageProvider : IStorageProvider
    {
        private int _counter;

        public Task<Resource> StoreRawAsync(RawDropData dropData, ResourceIngestOptions options, CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _counter);
            var fileName = string.IsNullOrWhiteSpace(dropData.OriginalSuggestedName)
                ? "raw-" + index + ".txt"
                : dropData.OriginalSuggestedName!;

            var resource = new Resource
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                SourceUri = "C:\\fake\\" + fileName,
                OriginalFileName = fileName,
                MimeType = "text/plain",
                FileSize = dropData.Text?.Length ?? 0,
                Source = options.Source,
                Privacy = options.Privacy,
                SyncPolicy = options.SyncPolicy,
                ProcessingModel = options.ProcessingModel,
                PermissionPresetId = options.PermissionPresetId,
                TitleOverride = options.TitleOverride
            };

            return Task.FromResult(resource);
        }
    }

    private sealed class InMemoryResourceStore : IResourceStore
    {
        private readonly ConcurrentDictionary<Guid, Resource> _resources = new();

        public Task UpsertAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            _resources[resource.Id] = resource;
            return Task.CompletedTask;
        }

        public Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _resources.TryGetValue(id, out var resource);
            return Task.FromResult(resource);
        }

        public Task<Resource?> GetByFeatureHashAsync(string featureHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(featureHash))
            {
                return Task.FromResult<Resource?>(null);
            }

            var resource = _resources.Values
                .FirstOrDefault(r => string.Equals(r.FeatureHash, featureHash, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(resource);
        }

        public Task<IReadOnlyList<Resource>> ListRecentAsync(
            int limit,
            IReadOnlyList<string>? tagFilters = null,
            bool applyConditionVisibility = true,
            CancellationToken cancellationToken = default)
        {
            var list = _resources.Values
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<Resource>>(list);
        }

        public Task<IReadOnlyList<Resource>> SearchAsync(
            string query,
            int limit,
            int offset,
            IReadOnlyList<string>? tagFilters = null,
            bool applyConditionVisibility = true,
            CancellationToken cancellationToken = default)
        {
            var list = _resources.Values
                .Skip(offset)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<Resource>>(list);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _resources.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySearchIndex : ISearchIndex
    {
        private readonly IResourceStore _store;

        public InMemorySearchIndex(IResourceStore store)
        {
            _store = store;
        }

        public Task IndexAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Resource>> QueryAsync(
            string query,
            int limit,
            int offset,
            IReadOnlyList<string>? tagFilters = null,
            bool applyConditionVisibility = true,
            CancellationToken cancellationToken = default)
        {
            return _store.SearchAsync(
                query,
                limit,
                offset,
                tagFilters,
                applyConditionVisibility,
                cancellationToken);
        }
    }

    private sealed class FixedResolver : IFormatConverterResolver
    {
        private readonly IFormatConverter _converter;

        public FixedResolver(IFormatConverter converter)
        {
            _converter = converter;
        }

        public IFormatConverter? Resolve(string mimeType)
        {
            return _converter;
        }
    }

    private sealed class SlowCountingConverter : IFormatConverter
    {
        private readonly int _delayMilliseconds;
        private int _inFlight;
        private int _maxObserved;

        public SlowCountingConverter(int delayMilliseconds)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public string Name => "slow-counting";

        public IReadOnlyCollection<string> SupportedMimeTypes { get; } = new[] { "text/plain" };

        public int MaxObservedConcurrency => _maxObserved;

        public async Task<ConversionResult> ConvertToFriendlyAsync(string inputPath, ConvertOptions options, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _inFlight);
            RecordMax(active);
            try
            {
                await Task.Delay(_delayMilliseconds, cancellationToken);
                return new ConversionResult
                {
                    ProcessedFilePath = inputPath,
                    ProcessedText = "converted"
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<ExtractedContent> ExtractContentAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            var content = new ExtractedContent
            {
                Paragraphs = new[]
                {
                    new ContentParagraph { Index = 0, Text = "converted" }
                }
            };
            return Task.FromResult(content);
        }

        public Task<string?> GenerateThumbnailAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        private void RecordMax(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxObserved);
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxObserved, active, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class EmptyAiProvider : IAIProvider
    {
        public Task<AIResult> AnalyzeAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AIResult.Empty);
        }
    }

    private sealed class NoOpThumbnailProvider : IThumbnailProvider
    {
        public Task<string?> GenerateAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class NoOpCloudSyncProvider : ICloudSyncProvider
    {
        public Task UploadAsync(Resource resource, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}