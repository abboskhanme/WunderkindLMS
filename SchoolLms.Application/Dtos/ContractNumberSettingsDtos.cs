namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Shartnoma raqamlash qoidasi — docs/modules/students-parity.md §2.10 (K-6).
//
//  `school_meta.contract_number_mode` — avtomatik ketma-ketlik yoki qo'lda
//  kiritish (SchoolLms.Domain.ContractNumberMode: "auto" | "manual").
// ===========================================================================

/// <summary>Joriy raqamlash rejimi — GET/PUT bir xil shakldan foydalanadi.</summary>
public record ContractNumberSettingsDto(string NumberMode);

/// <summary>Rejimni saqlash so'rovi.</summary>
public record SaveContractNumberSettingsRequest(string? NumberMode);
