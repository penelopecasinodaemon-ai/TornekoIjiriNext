namespace TornekoIjiriNext;

/// <summary>
/// オンライン編集項目の定義。
/// アドレス検証済みフラグは、解析・検証を完了した項目だけtrueにする。
/// </summary>
internal sealed record PineOnlineEditFieldDefinition(
    string GroupName,
    string FieldName,
    uint Address,
    int WidthBytes,
    uint InitialValue,
    bool AddressVerified,
    string Description = "");