using ResourceRouter.Core.Models;
using ResourceRouter.PluginSdk;

namespace ResourceRouter.Core.Abstractions;

public interface IProcessingConfigurationProvider
{
    ProcessingConfigurationSnapshot Resolve(Resource resource, IFormatConverter? converter);
}