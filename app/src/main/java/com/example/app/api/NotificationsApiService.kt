package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Query

interface NotificationsApiService {
    @GET("api/notifications")
    suspend fun getNotifications(
        @Query("login") login: String,
        @Query("take") take: Int = 50
    ): Response<NotificationsResponse>

    @POST("api/notifications/mark-read")
    suspend fun markRead(@Body request: MarkReadRequest): Response<MarkReadResponse>
}

