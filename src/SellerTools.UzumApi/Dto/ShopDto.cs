using System.Text.Json.Serialization;

namespace SellerTools.UzumApi.Dto;

// DTO для GET /v1/shops → array<OrganizationDto>. Только id + name (подтверждено спекой).
public class ShopDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
