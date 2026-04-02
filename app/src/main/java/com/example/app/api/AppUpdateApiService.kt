package com.example.app.api

import retrofit2.Response
import retrofit2.http.GET

interface AppUpdateApiService {
    @GET("api/app/latest")
    suspend fun getLatest(): Response<AppLatestResponse>
}

