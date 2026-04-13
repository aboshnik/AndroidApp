package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.DELETE
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Part
import retrofit2.http.Query
import okhttp3.MultipartBody

interface ChatApiService {
    @GET("api/chat/threads")
    suspend fun getThreads(
        @Query("login") login: String
    ): Response<ThreadsResponse>

    @GET("api/chat/colleagues/search")
    suspend fun searchColleagues(
        @Query("login") login: String,
        @Query("q") query: String? = null
    ): Response<ColleagueSearchResponse>

    @GET("api/chat/threads/{threadId}/messages")
    suspend fun getMessages(
        @Path("threadId") threadId: Int,
        @Query("login") login: String,
        @Query("take") take: Int = 80,
        @Query("beforeId") beforeId: Int? = null
    ): Response<MessagesResponse>

    @POST("api/chat/threads/{threadId}/messages")
    suspend fun sendMessage(
        @Path("threadId") threadId: Int,
        @Body body: SendMessageRequest
    ): Response<SendMessageResponse>

    @Multipart
    @POST("api/chat/threads/{threadId}/media")
    suspend fun uploadChatMedia(
        @Path("threadId") threadId: Int,
        @Query("login") login: String,
        @Part file: MultipartBody.Part
    ): Response<ChatMediaUploadResponse>

    @POST("api/chat/threads/direct/open")
    suspend fun openDirectThread(
        @Body body: OpenDirectThreadRequest
    ): Response<OpenDirectThreadResponse>

    @DELETE("api/chat/threads/{threadId}/messages/{messageId}")
    suspend fun deleteMessage(
        @Path("threadId") threadId: Int,
        @Path("messageId") messageId: Int,
        @Query("login") login: String
    ): Response<DeleteMessageResponse>

    /** POST вместо DELETE: Retrofit при 4xx часто отдаёт body=null; плюс часть прокси режет DELETE. */
    @POST("api/chat/threads/{threadId}/history/clear")
    suspend fun clearThreadHistory(
        @Path("threadId") threadId: Int,
        @Query("login") login: String
    ): Response<ClearThreadHistoryResponse>

    @GET("api/chat/bots/{botId}/profile")
    suspend fun getBotProfile(
        @Path("botId") botId: String,
        @Query("login") login: String
    ): Response<BotProfileResponse>

    @POST("api/chat/bots/{botId}/profile")
    suspend fun updateBotProfile(
        @Path("botId") botId: String,
        @Body body: UpdateBotProfileRequest
    ): Response<UpdateBotProfileResponse>

    @Multipart
    @POST("api/chat/bots/{botId}/avatar")
    suspend fun uploadBotAvatar(
        @Path("botId") botId: String,
        @Query("login") login: String,
        @Part file: MultipartBody.Part
    ): Response<UpdateBotProfileResponse>
}

