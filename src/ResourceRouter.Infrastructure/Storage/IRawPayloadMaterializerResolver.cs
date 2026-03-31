namespace ResourceRouter.Infrastructure.Storage;

public interface IRawPayloadMaterializerResolver
{
    IRawPayloadMaterializer Resolve(RawPayloadMaterializationContext context);
}
