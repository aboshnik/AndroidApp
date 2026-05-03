package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Query
import okhttp3.MultipartBody

interface CoinsApiService {
    @GET("api/coins/balance")
    suspend fun getBalance(
        @Query("login") login: String
    ): Response<CoinsBalanceResponse>

    @GET("api/coins/shop")
    suspend fun getShop(
        @Query("login") login: String
    ): Response<CoinsShopResponse>

    @POST("api/coins/shop/products")
    suspend fun createProduct(
        @Body request: ShopProductCreateRequest
    ): Response<CoinsShopResponse>

    @Multipart
    @POST("api/coins/shop/products/image")
    suspend fun uploadProductImage(
        @Query("login") login: String,
        @Part file: MultipartBody.Part
    ): Response<ShopImageUploadResponse>

    @POST("api/coins/shop/cart/add")
    suspend fun addToCart(
        @Body request: ShopCartAddRequest
    ): Response<CoinsCartResponse>

    @POST("api/coins/shop/cart/set-quantity")
    suspend fun setCartQuantity(
        @Body request: ShopCartSetQuantityRequest
    ): Response<CoinsCartResponse>

    @GET("api/coins/shop/cart")
    suspend fun getCart(
        @Query("login") login: String
    ): Response<CoinsCartResponse>

    @POST("api/coins/shop/cart/checkout")
    suspend fun checkout(
        @Body request: ShopCheckoutRequest
    ): Response<ShopCheckoutResponse>

    @GET("api/coins/shop/orders")
    suspend fun getOrders(
        @Query("login") login: String
    ): Response<ShopOrdersResponse>

    @GET("api/coins/shop/order-details")
    suspend fun getOrderDetails(
        @Query("login") login: String,
        @Query("orderNumber") orderNumber: String
    ): Response<ShopOrderDetailsResponse>
}
