// Copyright (c) 2015 Electronic Theatre Controls, Inc., http://www.etcconnect.com
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

#include "pch.h"
#include "EosUdp.h"
#include "EosUdp_Win.h"


using namespace Eos;
////////////////////////////////////////////////////////////////////////////////

EosUdpIn::EosUdpIn()
{
	m_RecvBuf = new char[EOS_UDP_RECV_BUF_LEN];
}

////////////////////////////////////////////////////////////////////////////////

EosUdpIn::~EosUdpIn()
{
	delete[] m_RecvBuf;
}

////////////////////////////////////////////////////////////////////////////////

void EosUdpIn::SetLogPrefix(const char *name, const char *ip, unsigned short port, std::string &logPrefix)
{
	if( ip )
		logPrefix = ip;
	else
		logPrefix.clear();

	size_t maxLen = strlen("255.255.255.255");
	if(logPrefix.size() > maxLen)
		logPrefix.resize(maxLen);

	if( name )
	{
		logPrefix.insert(0, " ");
		logPrefix.insert(0, name);
	}

	char buf[34];
	sprintf_s(buf, ":%u", port);
	logPrefix.append(buf);
}

////////////////////////////////////////////////////////////////////////////////

const char* EosUdpIn::GetLogPrefix(const std::string &logPrefix)
{
	return (logPrefix.c_str() ? logPrefix.c_str() : "");
}

////////////////////////////////////////////////////////////////////////////////

EosUdpIn* EosUdpIn::Create()
{
	return (new EosUdpIn_Win());
}

////////////////////////////////////////////////////////////////////////////////

EosUdpOut* EosUdpOut::Create()
{
	return (new EosUdpOut_Win());
}

////////////////////////////////////////////////////////////////////////////////
