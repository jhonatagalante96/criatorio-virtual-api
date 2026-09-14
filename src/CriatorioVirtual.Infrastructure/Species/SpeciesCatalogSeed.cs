using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;

namespace CriatorioVirtual.Infrastructure.Species;

internal static class SpeciesCatalogSeed
{
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.UnixEpoch;

    public static IReadOnlyCollection<SpeciesEntity> All { get; } =
    [
        Create(1, "Sporophila angolensis", "Curió"),
        Create(2, "Sporophila maximiliani", "Bicudo - verdadeiro"),
        Create(3, "Paroaria coronata", "Cardeal"),
        Create(4, "Paroaria dominicana", "Galo-da-campina"),
        Create(5, "Passerina cyanoides", "Azulão-da-amazônia"),
        Create(6, "Sicalis flaveola brasiliensis", "Canário-da-terra"),
        Create(7, "Sporophila caerulescens", "Coleiro-papa-capim"),
        Create(8, "Sporophila lineola", "Bigodinho"),
        Create(9, "Sporophila frontalis", "Pichochó"),
        Create(10, "Sporophila nigricollis", "Coleiro-baiano"),
        Create(11, "Zonotrichia capensis", "Tico-tico"),
        Create(12, "Sporophila maximiliani gigantirostris", "Bicudo-pantaneiro"),
        Create(13, "Sporophila maximiliani atrirostris", "Bicudo-do-bico-preto"),
        Create(14, "Coryphospingus cucullatus", "Tico-tico-rei"),
        Create(15, "Sporophila collaris", "Coleiro-do-brejo"),
        Create(16, "Sporophila plumbea", "Patativa-verdadeira"),
        Create(17, "Coryphospingus pileatus", "Tico-tico-rei-cinza"),
        Create(18, "Sporophila leucoptera", "Cigarra-rainha"),
        Create(19, "Sporophila falcirostris", "Cigarra-verdadeira"),
        Create(20, "Sicalis flaveola pelzelni", "Canário-chapinha"),
        Create(21, "Volatinia jacarina", "Tiziu"),
        Create(22, "Gubernatrix cristata", "Cardeal-amarelo"),
        Create(23, "Sporophila ruficollis", "Caboclinho-de-papo-escuro"),
        Create(24, "Sporophila bouvreuil", "Caboclinho"),
        Create(25, "Haplospiza unicolor", "Cigarra-bambu"),
        Create(26, "Sporophila minuta", "Caboclinho-lindo"),
        Create(27, "Sporophila albogularis", "Golinho"),
        Create(28, "Sporophila crassirostris", "Bicudinho"),
        Create(29, "Icterus jamacaii", "Corrupião"),
        Create(30, "Gnorimopsar chopi", "Graúna"),
        Create(31, "Molothrus oryzivorus", "Iraúna-grande"),
        Create(32, "Agelasticus thilius", "Sargento"),
        Create(33, "Cacicus chrysopterus", "Tecelão"),
        Create(34, "Cacicus cela", "Xexéu"),
        Create(35, "Cyanoloxia brissonii", "Azulão verdadeiro"),
        Create(36, "Saltator fuliginosus", "Pimentão"),
        Create(37, "Saltator similis", "Trinca-ferro-verdadeiro"),
        Create(38, "Saltator aurantiirostris", "Bico-duro"),
        Create(39, "Cyanoloxia glaucocaerulea", "Azulinho"),
        Create(40, "Saltator atricollis", "Bico-de-pimenta"),
        Create(41, "Carduelis magellanicus", "Pintassilgo"),
        Create(42, "Carduelis yarrellii", "Pintassilgo-do-nordeste"),
        Create(43, "Euphonia laniirostris", "Gaturamo-de-bico-grosso"),
        Create(44, "Turdus albicollis", "Carachué-coleira sabiá"),
        Create(45, "Turdus amaurochalinus", "Sabiá-pocá"),
        Create(46, "Turdus fumigatus", "Sabiá-da-mata"),
        Create(47, "Turdus rufiventris", "Sabiá-laranjeira"),
        Create(48, "Turdus leucomelas", "Sabiá-barranco"),
        Create(49, "Turdus flavipes", "Sabiá-una"),
        Create(50, "Stephanophorus diadematus", "Sanhaço-frade"),
        Create(51, "Thraupis sayaca", "Sanhaço-cinzento"),
        Create(52, "Saltator maximus", "Tempera-viola"),
        Create(53, "Schistochlamys ruficapillus", "Bico-de-veludo"),
        Create(54, "Ramphocelus bresilius", "Tiê-sangue"),
        Create(55, "Thraupis episcopus", "Sanhaço-da-amazônia"),
        Create(56, "Tachyphonus coronatus", "Tiê-preto"),
        Create(57, "Tangara seledon", "Saíra-sete-cores"),
        Create(58, "Thraupis palmarum", "Sanhaço-do-coqueiro"),
        Create(59, "Schistochlamys melanopis", "Sanhaço-de-coleira"),
        Create(60, "Mimus saturninus", "Sabiá-do-campo")
    ];

    private static SpeciesEntity Create(int number, string scientificName, string popularName) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{number:D12}"),
            CreatedAtUtc,
            scientificName,
            popularName,
            isActive: true,
            defaultImageFileName: $"{number:D4}.jpg",
            defaultImageContentType: "image/jpeg");
}
