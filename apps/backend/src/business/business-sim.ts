import { BUSINESS_PRODUCTS, BusinessProductCode, businessLevelConfig, expectedDemand } from '@hwo/shared-contracts';

export interface StockLine {
  productCode: BusinessProductCode;
  quantity: number;
  avgUnitCost: number;
  sellPrice: number;
}

export type SaleTip = 'PRICE_TOO_HIGH' | 'SOLD_OUT_MISSED_SALES' | 'SELLING_BELOW_COST' | 'HEALTHY_PROFIT' | 'NO_STOCK';

export interface SaleLineResult {
  productCode: BusinessProductCode;
  demand: number;
  unitsSold: number;
  price: number;
  revenue: number;
  costOfGoods: number;
  profit: number;
  stockLeft: number;
  tips: SaleTip[];
}

/** Short Thai explanations shown after each round so players learn cause and effect. */
export const SALE_TIP_TEXT_TH: Record<SaleTip, string> = {
  PRICE_TOO_HIGH: 'ราคาสูงกว่าราคาตลาด ลูกค้าซื้อน้อยลง ลองลดราคาดูนะ',
  SOLD_OUT_MISSED_SALES: 'ของหมดก่อนลูกค้าหมด เติมสต็อกเพิ่มจะขายได้มากขึ้น',
  SELLING_BELOW_COST: 'ราคาขายต่ำกว่าต้นทุน ยิ่งขายยิ่งขาดทุน',
  HEALTHY_PROFIT: 'ตั้งราคาและสต็อกได้ดี ร้านมีกำไร',
  NO_STOCK: 'ยังไม่มีสินค้าในร้าน ซื้อวัตถุดิบก่อนเริ่มขาย',
};

/**
 * One deterministic sales round. Pure function: no I/O, easy to unit-test and to explain to players.
 */
export function simulateSaleRound(level: number, stock: StockLine[]): SaleLineResult[] {
  const { salesCapacity } = businessLevelConfig(level);
  return stock.map((line) => {
    const demand = expectedDemand(line.productCode, line.sellPrice, level);
    const unitsSold = Math.min(line.quantity, demand, salesCapacity);
    const revenue = unitsSold * line.sellPrice;
    const costOfGoods = unitsSold * line.avgUnitCost;
    const profit = revenue - costOfGoods;
    const tips: SaleTip[] = [];
    if (line.quantity === 0) tips.push('NO_STOCK');
    else {
      const referenceDemand = expectedDemand(line.productCode, BUSINESS_PRODUCTS[line.productCode].basePrice, level);
      if (demand < referenceDemand * 0.5) tips.push('PRICE_TOO_HIGH');
      if (unitsSold === line.quantity && demand > line.quantity) tips.push('SOLD_OUT_MISSED_SALES');
      if (line.sellPrice <= line.avgUnitCost) tips.push('SELLING_BELOW_COST');
      if (profit > 0 && tips.length === 0) tips.push('HEALTHY_PROFIT');
    }
    return { productCode: line.productCode, demand, unitsSold, price: line.sellPrice, revenue, costOfGoods, profit, stockLeft: line.quantity - unitsSold, tips };
  });
}

/** Weighted-average unit cost after buying more stock. */
export function weightedAvgCost(oldQty: number, oldAvg: number, addQty: number, addCost: number): number {
  const total = oldQty + addQty;
  return total === 0 ? 0 : Math.round((oldQty * oldAvg + addQty * addCost) / total);
}
