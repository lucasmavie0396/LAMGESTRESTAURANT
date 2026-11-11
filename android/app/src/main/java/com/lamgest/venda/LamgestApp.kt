package com.lamgest.venda

import android.app.Application
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest

/**
 * O servidor está na rede Wi-Fi local, que pode não ter internet. Nesse caso o Android passa o tráfego para os dados móveis
 * e o servidor deixa de se ver: a app fica sempre presa ao Wi-Fi, tenha ou não internet.
 */
class LamgestApp : Application() {
    override fun onCreate() {
        super.onCreate()
        val redes = getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
        val pedido = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        redes.requestNetwork(pedido, object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                redes.bindProcessToNetwork(network)
            }

            override fun onLost(network: Network) {
                redes.bindProcessToNetwork(null)
            }
        })
    }
}
