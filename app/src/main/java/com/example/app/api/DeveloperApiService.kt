package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.POST

interface DeveloperApiService {
    @POST("api/admin/permissions")
    suspend fun setPermissions(@Body request: SetPermissionsRequest): Response<AdminActionResponse>

    @POST("api/admin/notify/test")
    suspend fun notifyTest(@Body request: NotifyTestRequest): Response<AdminActionResponse>

    @POST("api/admin/notify/update")
    suspend fun notifyUpdate(@Body request: NotifyUpdateRequest): Response<AdminActionResponse>
}

