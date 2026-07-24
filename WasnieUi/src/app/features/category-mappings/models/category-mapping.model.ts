// The enrichment lookup table: (InputField, InputValue) → Category. Rule triggers filter on the
// resulting Category, so the discriminating value living in the "wrong" origin column stops mattering.

/** The transaction attributes a mapping may read. ProductSku is tried first, ProductName as fallback. */
export enum CategoryInputField {
  ProductSku = 'ProductSku',
  ProductName = 'ProductName',
}

export interface CategoryMapping {
  id: string;
  inputField: CategoryInputField;
  inputValue: string;
  category: string;
}

export interface CreateCategoryMappingRequest {
  inputField: CategoryInputField;
  inputValue: string;
  category: string;
}

export interface UpdateCategoryMappingRequest {
  id: string;
  inputField: CategoryInputField;
  inputValue: string;
  category: string;
}
