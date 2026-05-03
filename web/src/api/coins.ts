import { apiClient } from './client'

export type CoinsBalanceResponse = {
  success: boolean
  message: string
  balance: {
    login: string
    balance: number
    nextPayoutDays: number
  } | null
}

export type CoinsShopItem = {
  id: string
  title: string
  price: number
  category: string
  description?: string | null
  imageUrl?: string | null
  stock?: number
  inCartQty?: number
}

export type CoinsShopResponse = {
  success: boolean
  message: string
  items: CoinsShopItem[]
}

export type ShopCartAddRequest = {
  login: string
  productId: number
  quantity?: number
}

export type CoinsCartResponse = {
  success: boolean
  message: string
  items: Array<{
    productId: number
    title: string
    imageUrl?: string | null
    price: number
    quantity: number
    lineTotal: number
  }> | null
  totalAmount: number
}

export type ShopCheckoutRequest = {
  login: string
}

export type ShopCheckoutResponse = {
  success: boolean
  message: string
  itemsCount: number
  balanceAfter: number
  totalSpent: number
}

export async function getCoinsBalance(login: string) {
  const { data } = await apiClient.get<CoinsBalanceResponse>('api/coins/balance', { params: { login } })
  return data
}

export async function getCoinsShop() {
  const { data } = await apiClient.get<CoinsShopResponse>('api/coins/shop')
  return data
}

export async function addToCart(body: ShopCartAddRequest) {
  const { data } = await apiClient.post<CoinsCartResponse>('api/coins/shop/cart/add', body)
  return data
}

export async function checkoutCart(body: ShopCheckoutRequest) {
  const { data } = await apiClient.post<ShopCheckoutResponse>('api/coins/shop/cart/checkout', body)
  return data
}

