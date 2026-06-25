#nullable enable

namespace Fuookami.Ospf.Framework.Persistence;
/// <summary>
/// 持久化 API 控制器接口 / Persistence API controller interface.
/// </summary>
public interface IPersistenceApiController {
    // todo: extract interface function from mongodb.PersistenceApiController
}

/// <summary>
/// ORM 持久化 API 控制器 / ORM persistence API controller.
/// </summary>
public sealed class OrmPersistenceApiController : IPersistenceApiController {
}
