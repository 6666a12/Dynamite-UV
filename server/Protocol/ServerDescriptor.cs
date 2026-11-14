namespace DynamiteUniverse.Server.Protocol;

public sealed record ServerDescriptor(
    string Protocol, int ProtocolVersion, Guid InstanceId, string Name,
    string ImplementationStage, ServerCapabilities Capabilities, ServerContracts Contracts);

public sealed record ServerCapabilities(bool Accounts, bool Catalog, bool PersonalRecords);
public sealed record ServerContracts(int Accounts, int Catalog, int PersonalRecords, string PackageFormat, string DigestAlgorithm);
