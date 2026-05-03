package com.example.app.api

import com.google.gson.annotations.SerializedName

data class CoinsBalanceItem(
    @SerializedName("login") val login: String,
    @SerializedName("balance") val balance: Int,
    @SerializedName("nextPayoutDays") val nextPayoutDays: Int
)

data class CoinsBalanceResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("balance") val balance: CoinsBalanceItem? = null
)

data class CoinsShopItem(
    @SerializedName("id") val id: String,
    @SerializedName("title") val title: String,
    @SerializedName("price") val price: Int,
    @SerializedName("category") val category: String,
    @SerializedName("description") val description: String? = null,
    @SerializedName("imageUrl") val imageUrl: String? = null,
    @SerializedName("stock") val stock: Int = 0,
    @SerializedName("inCartQty") val inCartQty: Int = 0
)

data class CoinsShopResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("items") val items: List<CoinsShopItem>? = null
)

data class ShopProductCreateRequest(
    @SerializedName("login") val login: String,
    @SerializedName("title") val title: String,
    @SerializedName("description") val description: String? = null,
    @SerializedName("price") val price: Int,
    @SerializedName("stock") val stock: Int,
    @SerializedName("imageUrl") val imageUrl: String? = null,
    @SerializedName("category") val category: String? = null
)

data class ShopCartAddRequest(
    @SerializedName("login") val login: String,
    @SerializedName("productId") val productId: Int,
    @SerializedName("quantity") val quantity: Int = 1
)

data class ShopCartSetQuantityRequest(
    @SerializedName("login") val login: String,
    @SerializedName("productId") val productId: Int,
    @SerializedName("quantity") val quantity: Int
)

data class CoinsCartItem(
    @SerializedName("productId") val productId: Int,
    @SerializedName("title") val title: String,
    @SerializedName("imageUrl") val imageUrl: String? = null,
    @SerializedName("price") val price: Int,
    @SerializedName("quantity") val quantity: Int,
    @SerializedName("lineTotal") val lineTotal: Int
)

data class CoinsCartResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("items") val items: List<CoinsCartItem>? = null,
    @SerializedName("totalAmount") val totalAmount: Int = 0
)

data class ShopImageUploadResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("imageUrl") val imageUrl: String? = null
)

data class ShopCheckoutRequest(
    @SerializedName("login") val login: String
)

data class ShopCheckoutResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("itemsCount") val itemsCount: Int = 0,
    @SerializedName("balanceAfter") val balanceAfter: Int = 0,
    @SerializedName("totalSpent") val totalSpent: Int = 0
)

data class ShopOrderItem(
    @SerializedName("orderNumber") val orderNumber: String,
    @SerializedName("itemsCount") val itemsCount: Int,
    @SerializedName("totalAmount") val totalAmount: Int,
    @SerializedName("createdAt") val createdAt: String
)

data class ShopOrdersResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("items") val items: List<ShopOrderItem>? = null
)

data class ShopOrderDetailItem(
    @SerializedName("productId") val productId: Int,
    @SerializedName("title") val title: String,
    @SerializedName("imageUrl") val imageUrl: String? = null,
    @SerializedName("price") val price: Int,
    @SerializedName("quantity") val quantity: Int,
    @SerializedName("lineTotal") val lineTotal: Int
)

data class ShopOrderDetailsItem(
    @SerializedName("orderNumber") val orderNumber: String,
    @SerializedName("itemsCount") val itemsCount: Int,
    @SerializedName("totalAmount") val totalAmount: Int,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("items") val items: List<ShopOrderDetailItem> = emptyList()
)

data class ShopOrderDetailsResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("order") val order: ShopOrderDetailsItem? = null
)
